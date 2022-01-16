using PrPolicy.Models;
using YamlDotNet.Serialization.NamingConventions;

namespace PrPolicy
{
    /// <summary>
    /// Class containg validation logic for the GitVersion file.
    /// </summary>
    public static class ContentValidator
    {
        /// <summary>
        /// Validate if the contents of the GitVersion.yml file are changed correctly.
        /// </summary>
        /// <param name="originalContents">The contents of the GitVersion.yml file from the main branch.</param>
        /// <param name="updatedContents">The contents of the GitVersion.yml file from the PR.</param>
        /// <returns>Wether the changes to the GitVersion file were valid.</returns>
        public static bool IsValidGitversion(string originalContents, string updatedContents)
        {
            // If the files are empty, it's not valid.
            if (string.IsNullOrEmpty(originalContents) || string.IsNullOrEmpty(updatedContents))
            {
                return false;
            }

            var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
                .WithNamingConvention(new HyphenatedNamingConvention())
                .IgnoreUnmatchedProperties()
                .Build();

            // Parse both YAML files to a POCO, so we can compare content.
            var originalYaml = deserializer.Deserialize<GitVersion>(originalContents);
            var updatedYaml = deserializer.Deserialize<GitVersion>(updatedContents);

            // If both files have the same version, the contents aren't really updated, but rather reverted to the original state.
            if (originalYaml.NextVersion == updatedYaml.NextVersion)
            {
                return false;
            }

            // Return wether the new YAML version is higher then the original.
            return ParseAndValidateGitVersion(originalYaml.NextVersion, updatedYaml.NextVersion);
        }

        private static bool ParseAndValidateGitVersion(string originalVersion, string nextVersion)
        {
            var originalVersionSplit = originalVersion.Split('.');
            var nextVersionSplit = nextVersion.Split('.');

            // If the next major version > original, then the updated file is valid.
            if (int.Parse(nextVersionSplit[0]) > int.Parse(originalVersionSplit[0]))
            {
                return true;
            }

            // If the next minor version > original, then the updated file is valid.
            if (int.Parse(nextVersionSplit[1]) > int.Parse(originalVersionSplit[1]))
            {
                return true;
            }

            // If the next patch version > original, then the updated file is valid.
            if (int.Parse(nextVersionSplit[2]) > int.Parse(originalVersionSplit[2]))
            {
                return true;
            }

            // Updated file has no updated GitVersion, so isn't valid.
            return false;
        }
    }
}