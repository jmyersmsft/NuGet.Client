// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

namespace NuGet.Configuration
{
    /// <summary>
    /// Represents credentials required to authenticate user within package source web requests.
    /// </summary>
    public class PackageSourceCredential
    {
        /// <summary>
        /// User name
        /// </summary>
        public string Username { get; }

        /// <summary>
        /// Password text as stored in config file. May be encrypted.
        /// </summary>
        public string PasswordText { get; }

        /// <summary>
        /// Indicates if password is stored in clear text.
        /// </summary>
        public bool IsPasswordClearText { get; }

        /// <summary>
        /// A list of HTTP auth types the credentials in this source should be used for. Useful values include negotiate, ntlm, basic.
        /// May be null, in which case all auth types are used.
        /// </summary>
        public IReadOnlyList<string> AuthTypeFilter { get; }

        /// <summary>
        /// Retrieves password in clear text. Decrypts on-demand.
        /// </summary>
        public string Password
        {
            get
            {
                if (PasswordText != null && !IsPasswordClearText)
                {
                    try
                    {
                        return EncryptionUtility.DecryptString(PasswordText);
                    }
                    catch (NotSupportedException e)
                    {
                        throw new NuGetConfigurationException(
                            string.Format(CultureInfo.CurrentCulture, Resources.UnsupportedDecryptPassword, Source), e);
                    }
                }
                else
                {
                    return PasswordText;
                }
            }
        }

        /// <summary>
        /// Associated source ID
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// Verifies if object contains valid data, e.g. not empty user name and password.
        /// </summary>
        /// <returns>True if credentials object is valid</returns>
        public bool IsValid() => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(PasswordText);

        public ICredentials GetCredentials()
        {
            return new AuthTypeFilteredCredentials(new NetworkCredential(Username, Password), AuthTypeFilter);
        }

        /// <summary>
        /// Instantiates the credential instance out of raw values read from a config file.
        /// </summary>
        /// <param name="source">Associated source ID (needed for reporting errors)</param>
        /// <param name="authTypeFilter">HTTP auth types these credentials should be used for. If null, all auth types are used.</param>
        /// <param name="username">User name</param>
        /// <param name="passwordText">Password as stored in config file</param>
        /// <param name="isPasswordClearText">Hints if password provided in clear text</param>
        public PackageSourceCredential(string source, IEnumerable<string> authTypeFilter, string username, string passwordText, bool isPasswordClearText)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (username == null)
            {
                throw new ArgumentNullException(nameof(username));
            }

            Source = source;
            Username = username;
            PasswordText = passwordText;
            IsPasswordClearText = isPasswordClearText;
            AuthTypeFilter = authTypeFilter?.ToArray();
        }

        /// <summary>
        /// Instantiates the credential instance out of raw values read from a config file.
        /// </summary>
        /// <param name="source">Associated source ID (needed for reporting errors)</param>
        /// <param name="username">User name</param>
        /// <param name="passwordText">Password as stored in config file</param>
        /// <param name="isPasswordClearText">Hints if password provided in clear text</param>
        public PackageSourceCredential(string source, string username, string passwordText, bool isPasswordClearText)
            : this(source, null, username, passwordText, isPasswordClearText)
        {
        }

        /// <summary>
        /// Creates new instance of credential object out values provided by user.
        /// </summary>
        /// <param name="source">Source ID needed for reporting errors if any</param>
        /// <param name="username">User name</param>
        /// <param name="password">Password text in clear</param>
        /// <param name="storePasswordInClearText">Hints if the password should be stored in clear text on disk.</param>
        /// <returns>New instance of <see cref="PackageSourceCredential"/></returns>
        public static PackageSourceCredential FromUserInput(string source, string username, string password, bool storePasswordInClearText)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (username == null)
            {
                throw new ArgumentNullException(nameof(username));
            }

            if (password == null)
            {
                throw new ArgumentNullException(nameof(password));
            }

            try
            {
                var passwordText = storePasswordInClearText ? password: EncryptionUtility.EncryptString(password);
                return new PackageSourceCredential(source, null, username, passwordText, storePasswordInClearText);
            }
            catch (NotSupportedException e)
            {
                throw new NuGetConfigurationException(
                    string.Format(CultureInfo.CurrentCulture, Resources.UnsupportedEncryptPassword, source), e);
            }
        }
    }
}