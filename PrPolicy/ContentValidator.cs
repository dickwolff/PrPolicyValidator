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

            // If both files are the same, the updated content isn't really updated, but reverted to the original state. 
            if (originalContents == updatedContents)
            {
                return false;
            }

            // Return valid by default.
            return true;
        }
    }
}