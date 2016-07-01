using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;


namespace NuGet.Configuration
{
    /// <summary>
    /// Wraps another ICredentials object by returning null if the authType is not in a specified allow list
    /// </summary>
    public class AuthTypeFilteredCredentials : ICredentials
    {
        private readonly string[] _authTypeFilter;
        private readonly ICredentials _innerCredential;

        /// <summary>
        /// Initializes a new AuthTypeFilteredCredentials
        /// </summary>
        /// <param name="innerCredential">Credential to delegate to</param>
        /// <param name="authTypeFilter">List of authTypes to respond to. May be null, in which case all auth types are allowed.</param>
        public AuthTypeFilteredCredentials(ICredentials innerCredential, IEnumerable<string> authTypeFilter)
        {
            if (innerCredential == null)
            {
                throw new ArgumentNullException(nameof(innerCredential));
            }

            _innerCredential = innerCredential;
            _authTypeFilter = authTypeFilter?.ToArray();
        }

        public NetworkCredential GetCredential(Uri uri, string authType)
        {
            return _authTypeFilter == null || _authTypeFilter.Any(x => StringComparer.OrdinalIgnoreCase.Equals(x, authType))
                ? _innerCredential.GetCredential(uri, authType)
                : null;
        }

        public static IEnumerable<string> ParseAuthTypes(string authTypeString)
        {
            if (authTypeString == null)
            {
                return null;
            }

            return authTypeString
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => !String.IsNullOrEmpty(x));
        }
    }
}