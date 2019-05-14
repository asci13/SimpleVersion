// Licensed under the MIT license. See https://kieranties.mit-license.org/ for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Newtonsoft.Json;
using SimpleVersion.Abstractions.Pipeline;
using SimpleVersion.Comparers;
using SVM = SimpleVersion.Model;

namespace SimpleVersion.Pipeline
{
    /// <summary>
    /// Resolves the configuration for the version calculation.
    /// </summary>
    public class ConfigurationContextProcessor : IVersionContextProcessor
    {
        private static readonly ConfigurationVersionLabelComparer _comparer = new ConfigurationVersionLabelComparer();

        /// <inheritdoc/>
        public void Apply(IVersionContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            if (!(context is VersionContext repoContext))
                throw new InvalidCastException($"Could not convert given context to {typeof(VersionContext)}");

            var config = ResolveConfiguration(repoContext.Repository.Head?.Tip, repoContext.Result.CanonicalBranchName);

            context.Configuration = config;

            var (height, branchHeight) = ResolveHeight(repoContext.Repository, config, repoContext.Result.CanonicalBranchName);

            context.Result.Height = height;
            context.Result.BranchHeight = branchHeight;
        }

        private static SVM.Configuration ResolveConfiguration(Commit commit, string canonicalBranch)
        {
            return GetConfiguration(commit, canonicalBranch)
                        ?? throw new InvalidOperationException($"Could not read '{Constants.VersionFileName}', has it been committed?");
        }

        private (int Height, int BranchHeight) ResolveHeight(IRepository repository, SVM.Configuration config, string canonicalBranch)
        {
            // Initialise count - The current commit counts, include offset
            var branchHeight = 1 + config.OffSet;
            var height = 1 + config.OffSet;

            var isResultBranch = config.Branches.Release.Any(p => Regex.IsMatch(canonicalBranch, p));

            // get the commits reachable from the current branch
            var commits = GetReachableCommits(repository, canonicalBranch).Skip(1).GetEnumerator();

            // Get the state of this tree to compare for diffs
            var tipTree = repository.Head.Tip.Tree;

            while (commits.MoveNext())
            {
                // Get the current tree
                var next = commits.Current.Tree;

                // Perform a diff
                var diff = repository.Diff.Compare<TreeChanges>(next, tipTree);

                // If a change to the file is found, stop counting
                if (HasVersionChange(diff, commits.Current, config, canonicalBranch))
                    break;

                if (isResultBranch)
                {
                    // Increment both heights
                    height++;
                    branchHeight++;
                }
                else
                {
                    // Check to see if parent is a merge
                    if (commits.Current.Parents.Count() > 1)
                    {
                        var shouldBreak = false;
                        foreach (var commit in commits.Current.Parents)
                        {
                            if (IsInReleaseBranch(repository, config, commit, out var parent))
                            {
                                shouldBreak = true;
                                var parentConfig = ResolveConfiguration(commits.Current, parent.CanonicalName);
                                var (_, parentBaseHeight) = ResolveHeight(repository, config, parent.CanonicalName);
                                height += parentBaseHeight;
                                break;
                            }
                        }

                        if (shouldBreak) break;
                    }
                    else
                    {
                        // If not a merge then release commits on directly on this branch
                        if (IsInReleaseBranch(repository, config, commits.Current, out var _))
                            height++;
                        else
                            branchHeight++;
                    }
                }
            }

            if (isResultBranch)
            {
                height = branchHeight;
            }

            return (height, branchHeight);
        }

        private static bool HasVersionChange(
            TreeChanges diff,
            Commit commit,
            SVM.Configuration config,
            string canonicalBranch)
        {
            if (diff.Any(d => d.Path == Constants.VersionFileName))
            {
                var commitConfig = GetConfiguration(commit, canonicalBranch);
                return commitConfig != null && !_comparer.Equals(config, commitConfig);
            }

            return false;
        }

        private static IEnumerable<Commit> GetReachableCommits(IRepository repo, string canonicalBranch)
        {
            var filter = new CommitFilter
            {
                FirstParentOnly = true,
                IncludeReachableFrom = canonicalBranch,
                SortBy = CommitSortStrategies.Reverse
            };

            return repo.Commits.QueryBy(filter).Reverse();
        }

        private static bool IsInReleaseBranch(IRepository repository, SVM.Configuration config, Commit commit, out Reference branch)
        {
            branch = repository.Refs
                .ReachableFrom(new[] { commit })
                .FirstOrDefault(x => x.IsReleaseBranch(config.Branches.Release));

            return branch != null;
        }

        private static SVM.Configuration GetConfiguration(Commit commit, string canonicalBranch)
        {
            var gitObj = commit?.Tree[Constants.VersionFileName]?.Target;
            if (gitObj == null)
                return null;

            var config = Read((gitObj as Blob).GetContentText());
            ApplyConfigOverrides(config, canonicalBranch);
            return config;
        }

        private static void ApplyConfigOverrides(SVM.Configuration config, string canonicalBranch)
        {
            if (config == null)
                return;

            var firstMatch = config.Branches
                .Overrides.FirstOrDefault(x => Regex.IsMatch(canonicalBranch, x.Match, RegexOptions.IgnoreCase));

            if (firstMatch != null)
            {
                if (firstMatch.Label != null)
                {
                    config.Label.Clear();
                    config.Label.AddRange(firstMatch.Label);
                }

                if (firstMatch.Metadata != null)
                {
                    config.Metadata.Clear();
                    config.Metadata.AddRange(firstMatch.Metadata);
                }
            }
        }

        private static SVM.Configuration Read(string rawConfiguration)
        {
            try
            {
                return JsonConvert.DeserializeObject<SVM.Configuration>(rawConfiguration);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch
            {
                // TODO handle logger of invalid parsing
                return null;
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }
    }
}
