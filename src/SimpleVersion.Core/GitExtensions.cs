using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Git = LibGit2Sharp;

namespace SimpleVersion
{
    public static class GitExtensions
    {
        public static bool IsReleaseBranch(this Git.Reference reference, IEnumerable<string> branchPatterns)
        {
            return branchPatterns.Any(p => Regex.IsMatch(reference.CanonicalName, p));
        }
    }
}
