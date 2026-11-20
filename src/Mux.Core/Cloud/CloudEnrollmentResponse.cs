namespace Mux.Core.Cloud
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Successful response of <c>POST /v1.0/api/machines/enroll</c>.
    /// </summary>
    public class CloudEnrollmentResponse
    {
        /// <summary>Machine identifier (prefix <c>mch_</c>).</summary>
        [JsonPropertyName("machineId")]
        public string MachineId { get; set; } = string.Empty;

        /// <summary>Machine secret (prefix <c>mcs_</c>), shown once.</summary>
        [JsonPropertyName("machineSecret")]
        public string MachineSecret { get; set; } = string.Empty;

        /// <summary>Machine display name the cloud stored.</summary>
        [JsonPropertyName("machineName")]
        public string MachineName { get; set; } = string.Empty;

        /// <summary>Account email, when the cloud returns it.</summary>
        [JsonPropertyName("accountEmail")]
        public string? AccountEmail { get; set; }
    }
}
