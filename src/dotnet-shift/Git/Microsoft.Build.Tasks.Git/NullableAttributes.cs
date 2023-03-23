// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the License.txt file in the project root for more information.

namespace System
{
    internal static class NullableString
    {
        public static bool IsNullOrEmpty([NotNullWhen(false)]string? str)
            => string.IsNullOrEmpty(str);

        public static bool IsNullOrWhiteSpace([NotNullWhen(false)]string? str)
            => string.IsNullOrWhiteSpace(str);
    }

    internal static class NullableDebug
    {
        /// <inheritdoc cref="Debug.Assert(bool)"/>
        [Conditional("DEBUG")]
        public static void Assert([DoesNotReturnIf(false)]bool b)
            => Debug.Assert(b);

        /// <inheritdoc cref="Debug.Assert(bool, string)"/>
        [Conditional("DEBUG")]
        public static void Assert([DoesNotReturnIf(false)]bool b, string message)
            => Debug.Assert(b, message);

    }
}
