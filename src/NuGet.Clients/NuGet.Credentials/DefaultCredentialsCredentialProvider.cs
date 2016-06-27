// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;

namespace NuGet.Credentials
{
    public class DefaultCredentialsCredentialProvider : ICredentialProvider
    {
        public string Id { get; } = $"{nameof(DefaultCredentialsCredentialProvider)}_{Guid.NewGuid()}";

        public Task<CredentialResponse> GetAsync(
            Uri uri,
            IWebProxy proxy,
            CredentialRequestType type,
            string message,
            bool isRetry,
            bool nonInteractive,
            CancellationToken cancellationToken)
        {
            if (isRetry)
            {
                return Task.FromResult(new CredentialResponse(CredentialStatus.ProviderNotApplicable));
            }

            return Task.FromResult(
                new CredentialResponse(
                    CredentialCache.DefaultNetworkCredentials,
                    CredentialStatus.Success));
        }
    }
}