using YamlDotNet.Serialization;

namespace PrPolicy.Models
{
    /// <summary>
    /// GitVersion YAML definition.
    /// </summary>
    public class GitVersion
    {
        [YamlMember(Alias = "next-version")]
        public string NextVersion { get; set; }
    }
}