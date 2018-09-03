﻿using System;
using System.Threading.Tasks;
using CaseOnline.Azure.WebJobs.Extensions.Mqtt.Config;
using CaseOnline.Azure.WebJobs.Extensions.Mqtt.Messaging;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.ManagedClient;

namespace CaseOnline.Azure.WebJobs.Extensions.Mqtt.Listeners
{
    /// <summary>
    /// Manages the state of the MQTT connection, an wrapper around MQTTNet.IManagedMqttClient. 
    /// </summary>
    public class MqttConnection : IMqttConnection
    {
        private IMqttClientFactory _mqttClientFactory;
        private MqttConfiguration _config;
        private ILogger _logger;
        private IManagedMqttClient _managedMqttClient;
        private object startupLock = new object();

        public MqttConnection(IMqttClientFactory mqttClientFactory, MqttConfiguration config, ILogger logger)
        {
            _mqttClientFactory = mqttClientFactory;
            _config = config;
            _logger = logger;
        }

        public event Func<MqttMessageReceivedEventArgs, Task> OnMessageEventHandler;

        /// <summary>
        /// Gets the current status of the connection.
        /// </summary>
        public bool Connected { get; private set; }

        public bool Connecting { get; private set; }

        /// <summary>
        /// Gets the descriptor for this Connection.
        /// </summary> ;
        public override string ToString()
        {
            return $"Connection for config: {_config.ToString()}, currently connected: {Connected}";
        }

        /// <summary>
        /// Opens the MQTT connection.
        /// </summary>
        public async Task StartAsync()
        {
            if (Connecting)
            {
                for (var i = 0; i < 3; i++)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    if (!Connecting || Connected)
                    {
                        break;
                    }
                }
            }
            try
            {
                lock (startupLock)
                {
                    if (_managedMqttClient != null)
                    {
                        return;
                    }
                    Connecting = true;
                    _managedMqttClient = _mqttClientFactory.CreateManagedMqttClient();
                    _managedMqttClient.ApplicationMessageReceived += ManagedMqttClientApplicationMessageReceived;
                    _managedMqttClient.ApplicationMessageProcessed += ManagedMqttClientApplicationMessageProcessed;
                    _managedMqttClient.Connected += ManagedMqttClientConnected;
                    _managedMqttClient.Disconnected += ManagedMqttClientDisconnected;
                }
                await _managedMqttClient.StartAsync(_config.Options).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogCritical($"Exception while setting up the mqttclient to {this}", e);
                throw new MqttConnectionException($"Exception while setting up the mqttclient to {this}", e);
            }
        }

        private void ManagedMqttClientApplicationMessageProcessed(object sender, ApplicationMessageProcessedEventArgs e)
        {
            if (e.HasFailed)
            {
                _logger.LogError($"Message could not be processed for {this}, message: '{e.Exception?.Message}'", e.Exception);
            }
        }

        private void ManagedMqttClientDisconnected(object sender, MqttClientDisconnectedEventArgs e)
        {
            Connected = false;
            Connecting = false;
            _logger.LogWarning($"MqttConnection Disconnected, previous connectivity state '{e.ClientWasConnected}' for {this}, message: '{e.Exception?.Message}'", e.Exception);
        }

        private void ManagedMqttClientConnected(object sender, MqttClientConnectedEventArgs e)
        {
            Connected = true;
            Connecting = false;
            _logger.LogInformation($"MqttConnection Connected for {this}");
        }

        private void ManagedMqttClientApplicationMessageReceived(object sender, MqttApplicationMessageReceivedEventArgs mqttApplicationMessageReceivedEventArgs)
        {
            _logger.LogDebug($"MqttConnection receiving message for {this}");
            try
            {
                var qos = (MqttQualityOfServiceLevel)Enum.Parse(typeof(MqttQualityOfServiceLevel), mqttApplicationMessageReceivedEventArgs.ApplicationMessage.QualityOfServiceLevel.ToString());

                var mqttMessage = new MqttMessage(
                    mqttApplicationMessageReceivedEventArgs.ApplicationMessage.Topic,
                    mqttApplicationMessageReceivedEventArgs.ApplicationMessage.Payload,
                    qos,
                    mqttApplicationMessageReceivedEventArgs.ApplicationMessage.Retain);

                OnMessageEventHandler(new MqttMessageReceivedEventArgs(mqttMessage)).Wait();
            }
            catch (Exception e)
            {
                _logger.LogCritical($"Exception while processing message for {this}", e);
                throw new MqttConnectionException($"Exception while processing message for {this}", e);
            }
        }

        /// <summary>
        /// Subscribe to one or more topics.
        /// </summary>
        /// <param name="topics">The topics to subscribe to.</param>
        public async Task SubscribeAsync(TopicFilter[] topics)
        {
            if (_managedMqttClient == null)
            {
                throw new MqttConnectionException("Connection not open, please use StartAsync first!");
            }
            await _managedMqttClient.SubscribeAsync(topics).ConfigureAwait(false);
        }

        /// <summary>
        /// Unsubscribe to one or more topics.
        /// </summary>
        /// <param name="topics">The topics to unsubscribe from.</param>
        public async Task UnubscribeAsync(string[] topics)
        {
            if (_managedMqttClient == null)
            {
                return;
            }
            await _managedMqttClient.UnsubscribeAsync(topics).ConfigureAwait(false);
        }

        /// <summary>
        /// Publish a message on to the MQTT broker.
        /// </summary>
        /// <param name="message">The message to publish.</param>
        public async Task PublishAsync(MqttApplicationMessage message)
        {
            if (_managedMqttClient == null)
            {
                throw new MqttConnectionException("Connection not open, please use StartAsync first!");
            }
            await _managedMqttClient.PublishAsync(message).ConfigureAwait(false);
        }

        /// <summary>
        /// Close the MQTT connection.
        /// </summary>
        public Task StopAsync()
        {
            if (_managedMqttClient == null)
            {
                return Task.CompletedTask;
            }

            _managedMqttClient.StopAsync().Wait();
            _managedMqttClient.Dispose();
            _managedMqttClient = null;

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_managedMqttClient != null)
                {
                    _managedMqttClient.Dispose();
                    _managedMqttClient = null;
                }
            }
        }
    }
}
