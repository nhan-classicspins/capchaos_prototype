using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ClassicSpins.BuildTools
{
    /// <summary>
    /// Composes artifact file names, and owns the version-string rule that feeds them.
    /// Deliberately pure and free of Unity API calls so it can be unit tested, and so
    /// both rules live in exactly one place: the packaging shell scripts read finished
    /// names out of build-info.env rather than re-deriving them.
    /// </summary>
    public static class BuildNaming
    {
        /// <summary>UTC, minute resolution: yyMMddHHmm.</summary>
        public const string TimestampFormat = "yyMMddHHmm";

        /// <summary>Most parts a version string may have: 1, 1.0 or 1.0.0.</summary>
        public const int MaxVersionParts = 3;

        /// <summary>Digits per part. Nine keeps every part inside an int.</summary>
        public const int MaxVersionPartDigits = 9;

        private const string AllowedPunctuation = "._-";

        public static string ToBuildTimestamp(DateTime timestampUtc) =>
            timestampUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture);

        /// <summary>
        /// True when <paramref name="version"/> is usable as the version both stores accept:
        /// one to three dot-separated non-negative integers, such as <c>1</c>, <c>1.0</c> or
        /// <c>1.0.0</c>. Android's versionName would take anything, but iOS's
        /// CFBundleShortVersionString would not, and App Store Connect only says so at upload
        /// time — long after the build. Checking the stricter of the two rules up front turns
        /// that into a one-second usage error. <paramref name="error"/> is a sentence that
        /// reads correctly after either "--version" or "Version".
        /// </summary>
        public static bool IsValidVersion(string version, out string error)
        {
            if (string.IsNullOrEmpty(version))
            {
                error = "must not be empty.";
                return false;
            }

            string[] parts = version.Split('.');
            if (parts.Length > MaxVersionParts)
            {
                error = $"must have at most {MaxVersionParts} parts, e.g. 1.0.0, got '{version}'.";
                return false;
            }

            foreach (string part in parts)
            {
                if (part.Length == 0 || part.Any(c => c < '0' || c > '9'))
                {
                    error = $"must be dot-separated numbers, e.g. 1.0.0, got '{version}'.";
                    return false;
                }

                if (part.Length > MaxVersionPartDigits)
                {
                    error = $"must have at most {MaxVersionPartDigits} digits per part, got '{version}'.";
                    return false;
                }

                // '1.02' and '1.2' are the same version to the stores but two different artifact
                // names, so one spelling has to win.
                if (part.Length > 1 && part[0] == '0')
                {
                    error = $"must not have leading zeros, e.g. 1.2 rather than 1.02, got '{version}'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// Produces <c>{prefix}_{platformTag}_{version}-{buildNumber}{suffix}[_{method}]_{timestamp}</c>,
        /// for example <c>WoolGather_iOS_0.1.0-42_appstore_2608111530</c>. The extension
        /// is added by the caller. <paramref name="suffix"/> and <paramref name="method"/>
        /// are optional; every part is sanitised to characters that are safe in a file name.
        /// </summary>
        public static string ComposeArtifactName(string prefix, string platformTag, string version,
            string buildNumber, string suffix, string method, DateTime timestampUtc)
        {
            string safePrefix = RequireSanitized(prefix, nameof(prefix));
            string safePlatform = RequireSanitized(platformTag, nameof(platformTag));
            string safeVersion = RequireSanitized(version, nameof(version));
            string safeBuildNumber = RequireSanitized(buildNumber, nameof(buildNumber));

            var parts = new List<string>(5)
            {
                safePrefix,
                safePlatform,
                safeVersion + "-" + safeBuildNumber + Sanitize(suffix)
            };

            string safeMethod = Sanitize(method);
            if (safeMethod.Length > 0)
                parts.Add(safeMethod);

            parts.Add(ToBuildTimestamp(timestampUtc));
            return string.Join("_", parts);
        }

        /// <summary>
        /// Keeps ASCII letters, digits, dot, underscore and hyphen; collapses every other
        /// run of characters into a single hyphen. Needed because names flow in from
        /// project settings — <c>productName</c> here is "Wool Gather", with a space.
        /// </summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            bool pendingSeparator = false;

            foreach (char c in value)
            {
                bool isAllowed = (c < 128 && char.IsLetterOrDigit(c)) || AllowedPunctuation.IndexOf(c) >= 0;
                if (isAllowed)
                {
                    builder.Append(c);
                    pendingSeparator = false;
                }
                else if (!pendingSeparator)
                {
                    builder.Append('-');
                    pendingSeparator = true;
                }
            }

            return builder.ToString().Trim('-');
        }

        private static string RequireSanitized(string value, string parameterName)
        {
            string sanitized = Sanitize(value);
            if (sanitized.Length == 0)
                throw new ArgumentException($"'{parameterName}' must contain at least one file-name-safe character.", parameterName);

            return sanitized;
        }
    }
}
