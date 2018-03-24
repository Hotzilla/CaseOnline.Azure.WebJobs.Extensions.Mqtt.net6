using System;
using Microsoft.Azure.WebJobs;
using System.Text;
using Microsoft.Extensions.Logging;
using CaseOnline.Azure.WebJobs.Extensions.Mqtt;
using Newtonsoft.Json;
using MQTTnet;
using MQTTnet.Protocol;
using ExampleFunction.AdvancedConfig;

namespace ExampleFunctions
{
    public static class ExampleFunctions
    {
        [FunctionName("SimpleFunction")]
        public static void SimpleFunction(
            [MqttTrigger(new[] { "owntracks/kees/kees01", "owntracks/marleen/marleen01" })]PublishedMqttMessage message,
            ILogger log,
            [Table("Locations", Connection = "StorageConnectionAppSetting")] out Trail trail)
        {
            var body = Encoding.UTF8.GetString(message.Message);

            log.LogInformation($"Simple: message from topic {message.Topic} body: {body}");

            trail = JsonConvert.DeserializeObject<Trail>(body);
            trail.PartitionKey = message.Topic.Replace("/", "_");
            trail.RowKey = DateTime.Now.Ticks.ToString();
            trail.QosLevel = message.QosLevel.ToString();
            trail.Retain = message.Retain;
        }

        [FunctionName("AdvancedFunction")]
        public static void AdvancedFunction(
            [MqttTrigger(typeof(ExampleMqttConfigProvider))]PublishedMqttMessage message,
            ILogger log)
        {
            var body = Encoding.UTF8.GetString(message.Message);

            log.LogInformation($"Advanced: message from topic {message.Topic} body: {body}"); 
        }
    }
}