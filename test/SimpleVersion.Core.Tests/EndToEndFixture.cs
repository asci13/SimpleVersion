// Licensed under the MIT license. See https://kieranties.mit-license.org/ for full license information.

using FluentAssertions;
using GitTools.Testing;
using SimpleVersion.Pipeline.Formatting;
using System.Collections.Generic;
using Xunit;

namespace SimpleVersion.Core.Tests
{
    public class EndToEndFixture
    {
        [Fact]
        public void Master_Feature_Unique()
        {
            var config = new Model.Configuration
            {
                Version = "1.0.0",
                Branches =
                {
                    Release =
                    {
                        "^refs/heads/master$",
                    }
                }
            };

            using (var fixture = new SimpleVersionRepositoryFixture(config))
            using (EnvrionmentContext.NoBuildServer())
            {
                // Make some extra commits on master
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // branch to a feature branch
                fixture.BranchTo("feature/PBI-319594-GitVersionDeprecation");
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // Act
                var result = GetResult(fixture);
                result.Height.Should().Be(5);
                result.BranchHeight.Should().Be(3);
            }
        }

        [Fact]
        public void Master_Merge_Feature_Unique()
        {
            var config = new Model.Configuration
            {
                Version = "1.0.0",
                Branches =
                {
                    Release =
                    {
                        "^refs/heads/master$"
                    }
                }
            };

            using (var fixture = new SimpleVersionRepositoryFixture(config))
            using (EnvrionmentContext.NoBuildServer())
            {
                // Make some extra commits on master
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // branch to a feature branch
                fixture.BranchTo("feature/testing");
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // Merge back to master
                fixture.Checkout("master");
                fixture.MergeNoFF("feature/testing");

                // Act
                var result = GetResult(fixture);
                result.Height.Should().Be(5);
                result.BranchHeight.Should().Be(5);
            }
        }

        [Fact]
        public void Master_Release_Unique()
        {
            var config = new Model.Configuration
            {
                Version = "1.0.0",
                Branches =
                {
                    Release =
                    {
                        "^refs/heads/master$",
                        "^refs/heads/release/.+$",
                    }
                }
            };

            using (var fixture = new SimpleVersionRepositoryFixture(config))
            using (EnvrionmentContext.NoBuildServer())
            {
                // Make some extra commits on master
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // branch to a release branch
                fixture.BranchTo("release/1.0");
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // Act
                var result = GetResult(fixture);
                result.Height.Should().Be(7);
                result.BranchHeight.Should().Be(7);
            }
        }

        [Fact]
        public void Master_Feature_Preview_Unique()
        {
            var config = new Model.Configuration
            {
                Version = "1.0.0",
                Branches =
                {
                    Release =
                    {
                        "^refs/heads/master$",
                        "^refs/heads/preview/.+$",
                    }
                }
            };

            using (var fixture = new SimpleVersionRepositoryFixture(config))
            using (EnvrionmentContext.NoBuildServer())
            {
                // Make some extra commits on master
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // branch to a preview branch
                fixture.BranchTo("preview/1.1");
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // start a feature from master
                fixture.Checkout("master");
                fixture.BranchTo("feature/testing");
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // merge preview changes into feature
                fixture.MergeNoFF("preview/1.1");
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // Act
                var result = GetResult(fixture);
                result.Height.Should().Be(11);
                result.BranchHeight.Should().Be(3);
            }
        }

        // https://github.com/Kieranties/SimpleVersion/issues/71
        [Fact]
        public void Override_Branches_Do_Not_Work_If_Asterisk_Used_In_Label()
        {
            // Create the configuration model
            var config = new Model.Configuration
            {
                Version = "1.0.0",
                Label = { "r*" },
                Branches =
                {
                    Release =
                    {
                        "^refs/heads/master$",
                        "^refs/heads/release/.+$",
                        "^refs/heads/feature/.+$"
                    },
                    Overrides =
                    {
                        new Model.BranchConfiguration
                        {
                            Match = "^refs/heads/feature/.+$",
                            Label = new List<string> { "{shortbranchname}" }
                        }
                    }
                }
            };

            // Arrange
            using (var fixture = new SimpleVersionRepositoryFixture(config))
            using (EnvrionmentContext.NoBuildServer())
            {
                // Make some extra commits on master
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // branch to a feature branch
                fixture.BranchTo("feature/PBI-319594-GitVersionDeprecation");
                fixture.MakeACommit();
                fixture.MakeACommit();
                fixture.MakeACommit();

                // Act
                var result = GetResult(fixture);
                var semver1 = result.Formats[Semver1FormatProcess.FormatKey];
                var semver2 = result.Formats[Semver2FormatProcess.FormatKey];

                // Assert
                semver1.Should().Be("1.0.0-featurePBI319594GitVersionDeprecation-0007");
                semver2.Should().Be("1.0.0-featurePBI319594GitVersionDeprecation.7");
            }
        }

        private static Model.VersionResult GetResult(RepositoryFixtureBase repo) => VersionCalculator.Default().GetResult(repo.RepositoryPath);
    }
}
