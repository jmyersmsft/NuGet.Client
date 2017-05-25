﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;

namespace NuGet.PackageManagement
{
    /// <summary>
    /// Abstracts the logic to get a package stream for a given package identity from a given source repository
    /// </summary>
    public static class PackageDownloader
    {
        /// <summary>
        /// Asynchronously returns a <see cref="DownloadResourceResult" /> for a given package identity
        /// and enumerable of source repositories.
        /// </summary>
        /// <param name="sources">An enumerable of source repositories.</param>
        /// <param name="packageIdentity">A package identity.</param>
        /// <param name="downloadContext">A package download context.</param>
        /// <param name="globalPackagesFolder">A global packages folder path.</param>
        /// <param name="logger">A logger.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.
        /// The task result (<see cref="Task{TResult}.Result" />) returns a <see cref="DownloadResourceResult" />
        /// instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="sources" />
        /// is either <c>null</c> or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="packageIdentity" />
        /// is either <c>null</c> or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="downloadContext" />
        /// is either <c>null</c> or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="logger" />
        /// is either <c>null</c> or empty.</exception>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="token" />
        /// is cancelled.</exception>
        public static async Task<DownloadResourceResult> GetDownloadResourceResultAsync(
            IEnumerable<SourceRepository> sources,
            PackageIdentity packageIdentity,
            PackageDownloadContext downloadContext,
            string globalPackagesFolder,
            ILogger logger,
            CancellationToken token)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            if (packageIdentity == null)
            {
                throw new ArgumentNullException(nameof(packageIdentity));
            }

            if (downloadContext == null)
            {
                throw new ArgumentNullException(nameof(downloadContext));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            var failedTasks = new List<Task<DownloadResourceResult>>();
            var tasksLookup = new Dictionary<Task<DownloadResourceResult>, SourceRepository>();

            var linkedTokenSource = PluginCancellationTokenSource.CreateLinkedTokenSource(token, $"{nameof(PackageDownloader)} {packageIdentity}");
            var linkedToken = linkedTokenSource.Token;
            try
            {
                // Create a group of local sources that will go first, then everything else.
                var groups = new Queue<List<SourceRepository>>();
                var localGroup = new List<SourceRepository>();
                var otherGroup = new List<SourceRepository>();

                foreach (var source in sources)
                {
                    if (source.PackageSource.IsLocal)
                    {
                        localGroup.Add(source);
                    }
                    else
                    {
                        otherGroup.Add(source);
                    }
                }

                groups.Enqueue(localGroup);
                groups.Enqueue(otherGroup);

                while (groups.Count > 0)
                {
                    token.ThrowIfCancellationRequested();

                    var sourceGroup = groups.Dequeue();
                    var tasks = new List<Task<DownloadResourceResult>>();

                    foreach (var source in sourceGroup)
                    {
                        var task = GetDownloadResourceResultAsync(
                            source,
                            packageIdentity,
                            downloadContext,
                            globalPackagesFolder,
                            logger,
                            linkedTokenSource.Token);

                        tasksLookup.Add(task, source);
                        tasks.Add(task);
                    }

                    while (tasks.Any())
                    {
                        var completedTask = await Task.WhenAny(tasks);

                        if (completedTask.Status == TaskStatus.RanToCompletion)
                        {
                            tasks.Remove(completedTask);

                            // Cancel the other tasks, since, they may still be running
                            linkedTokenSource.Cancel("Canceling losers of race to provide package");

                            if (tasks.Any())
                            {
                                // NOTE: Create a Task out of remainingTasks which waits for all the tasks to complete
                                // and disposes the linked token source safely. One of the tasks could try to access
                                // its incoming CancellationToken to register a callback. If the linkedTokenSource was
                                // disposed before being accessed, it will throw an ObjectDisposedException.
                                // At the same time, we do not want to wait for all the tasks to complete before
                                // before this method returns with a DownloadResourceResult.
                                var remainingTasks = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await Task.WhenAll(tasks);
                                    }
                                    catch
                                    {
                                        // Any exception from one of the remaining tasks is not actionable.
                                        // And, this code is running on the threadpool and the task is not awaited on.
                                        // Catch all and do nothing.
                                    }
                                    finally
                                    {
                                        linkedTokenSource.Dispose();
                                    }
                                });
                            }

                            return completedTask.Result;
                        }
                        else
                        {
                            token.ThrowIfCancellationRequested();

                            // In this case, completedTask did not run to completion.
                            // That is, it faulted or got canceled. Remove it, and try Task.WhenAny again
                            tasks.Remove(completedTask);
                            failedTasks.Add(completedTask);
                        }
                    }
                }

                // no matches were found
                var errors = new StringBuilder();

                errors.AppendLine(string.Format(CultureInfo.CurrentCulture,
                    Strings.UnknownPackageSpecificVersion, packageIdentity.Id,
                    packageIdentity.Version.ToNormalizedString()));

                foreach (var task in failedTasks)
                {
                    var message = ExceptionUtilities.DisplayMessage(task.Exception);

                    errors.AppendLine($"  {tasksLookup[task].PackageSource.Source}: {message}");

                    if (NuGetTaskCompletionSource.All.TryGetValue(task, out var tcsHolder))
                    {
                        tcsHolder.Dump(errors);
                    }
                }

                if (NuGetTaskCompletionSource.All2.TryGetValue(linkedToken, out var tcsBag))
                {
                    foreach (var taskCompletionSourceHolder in tcsBag)
                    {
                        taskCompletionSourceHolder.Dump(errors, "TCS: ");
                    }
                }

                errors.AppendLine($"{failedTasks.Count}/{tasksLookup.Count} failed.");

                Debug.Print($"FailedTaskSummary:\n{errors}");

                throw new FatalProtocolException(errors.ToString());
            }
            catch
            {
                linkedTokenSource.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Asynchronously returns a <see cref="DownloadResourceResult" /> for a given package identity
        /// and source repository.
        /// </summary>
        /// <param name="sourceRepository">A source repository.</param>
        /// <param name="packageIdentity">A package identity.</param>
        /// <param name="downloadContext">A package download context.</param>
        /// <param name="globalPackagesFolder">A global packages folder path.</param>
        /// <param name="logger">A logger.</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>A task that represents the asynchronous operation.
        /// The task result (<see cref="Task{TResult}.Result" />) returns a <see cref="DownloadResourceResult" />
        /// instance.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="sourceRepository" />
        /// is either <c>null</c> or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="packageIdentity" />
        /// is either <c>null</c> or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="downloadContext" />
        /// is either <c>null</c> or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="logger" />
        /// is either <c>null</c> or empty.</exception>
        /// <exception cref="OperationCanceledException">Thrown if <paramref name="token" />
        /// is cancelled.</exception>
        public static async Task<DownloadResourceResult> GetDownloadResourceResultAsync(
            SourceRepository sourceRepository,
            PackageIdentity packageIdentity,
            PackageDownloadContext downloadContext,
            string globalPackagesFolder,
            ILogger logger,
            CancellationToken token)
        {
            if (sourceRepository == null)
            {
                throw new ArgumentNullException(nameof(sourceRepository));
            }

            if (packageIdentity == null)
            {
                throw new ArgumentNullException(nameof(packageIdentity));
            }

            if (downloadContext == null)
            {
                throw new ArgumentNullException(nameof(downloadContext));
            }

            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            var downloadResource = await sourceRepository.GetResourceAsync<DownloadResource>(token);

            if (downloadResource == null)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.DownloadResourceNotFound,
                        sourceRepository.PackageSource.Source));
            }

            token.ThrowIfCancellationRequested();

            DownloadResourceResult result;
            try
            {
                result = await downloadResource.GetDownloadResourceResultAsync(
                   packageIdentity,
                   downloadContext,
                   globalPackagesFolder,
                   logger,
                   token);
            }
            catch (OperationCanceledException)
            {
                result = new DownloadResourceResult(DownloadResourceResultStatus.Cancelled);
            }

            if (result == null)
            {
                throw new FatalProtocolException(string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.DownloadStreamNotAvailable,
                    packageIdentity,
                    sourceRepository.PackageSource.Source));
            }

            if (result.Status == DownloadResourceResultStatus.Cancelled)
            {
                throw new RetriableProtocolException(string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.PackageCancelledFromSource,
                    packageIdentity,
                    sourceRepository.PackageSource.Source));
            }

            if (result.Status == DownloadResourceResultStatus.NotFound)
            {
                throw new FatalProtocolException(string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.PackageNotFoundOnSource,
                    packageIdentity,
                    sourceRepository.PackageSource.Source));
            }

            if (result.PackageReader == null)
            {
                result.PackageStream.Seek(0, SeekOrigin.Begin);
                var packageReader = new PackageArchiveReader(result.PackageStream);
                result.PackageStream.Seek(0, SeekOrigin.Begin);
                result = new DownloadResourceResult(result.PackageStream, packageReader, sourceRepository.PackageSource.Source);
            }
            else if (result.Status != DownloadResourceResultStatus.AvailableWithoutStream)
            {
                // bind the source
                result = new DownloadResourceResult(result.PackageStream, result.PackageReader, sourceRepository.PackageSource.Source);
            }

            return result;
        }
    }
}