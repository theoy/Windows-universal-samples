//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

using System;
using System.Threading;
using System.Threading.Tasks;
using VoipTasks.BackgroundOperations;
using VoipTasks.Helpers;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Background;
using Windows.Foundation.Collections;

namespace VoipTasks
{
    public sealed class CallRtcTask : IBackgroundTask
    {
        static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(5.0);

        BackgroundTaskDeferral _deferral;
        Guid instanceId;
        CancellationTokenSource keepAliveLoopController = new CancellationTokenSource();

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();
            instanceId = taskInstance.InstanceId;
            Log.WriteLine($"Activating CallRtcTask '{instanceId}'");

            Current.RTCTask = this;
            // Register for Task Cancel callback
            taskInstance.Canceled += TaskInstance_Canceled;

            RunKeepAliveRequestLoop();
        }

        private void TaskInstance_Canceled(IBackgroundTaskInstance sender, BackgroundTaskCancellationReason reason)
        {
            if (_deferral != null)
            {
                _deferral.Complete();
                Log.WriteLine($"Windows requested cancel for CallRtcTask '{instanceId}' (reason={reason}), cancelling.");
            }
            else
            {
                Log.WriteLine($"Windows requested cancel for CallRtcTask '{instanceId}' (reason={reason}), ignoring because already completed.");
            }

            keepAliveLoopController?.Cancel();
            keepAliveLoopController = null;

            Current.RTCTask = null;
        }

        public void CompleteNormally()
        {
            Log.WriteLine($"Ended call / completed deferral for CallRtcTask '{instanceId}'");
            _deferral?.Complete();
            keepAliveLoopController?.Cancel();

            _deferral = null;
            keepAliveLoopController = null;
        }

        /// <summary>
        /// Sends keepAlive requests for the given half-interval. For each half-interval
        /// a new request is sent to the UI process' keepAlive service. We also send a full
        /// duration amount (equal to 2x Half Interval), so that usually two requests are active
        /// in staggered amounts. This will hopefully ensure that at all times, even if one
        /// request is ending, there is another staggered request that is midway through its
        /// duration.
        /// </summary>
        async void RunKeepAliveRequestLoop()
        {
            CancellationToken cancelToken = keepAliveLoopController.Token;
            try
            {
                // Send a request, then wait a half interval (cancellable), then send again
                while (true)
                {
                    cancelToken.ThrowIfCancellationRequested();

                    // fire a request -- not, this is not awaited, we don't care about response
                    // as much as methodically sending requests for every half-interval.
                    var requestTask = FireSingleKeepAliveRequest(cancelToken);

                    await Task.WhenAll(requestTask, Task.Delay(RequestInterval, cancelToken));
                }
            }
            // cancel is normal execution for this loop, we expect to be canceled, and then catch it here.
            catch (OperationCanceledException)
            {
            }
        }

        async Task FireSingleKeepAliveRequest(CancellationToken cancelToken)
        {
            using (var appConnection = new AppServiceConnection())
            {
                appConnection.AppServiceName = BackgroundOperation.KeepAliveServiceTaskName;
                appConnection.PackageFamilyName = Windows.ApplicationModel.Package.Current.Id.FamilyName;

                var connectionStatus = await appConnection.OpenAsync();
                if (connectionStatus != AppServiceConnectionStatus.Success)
                {
                    Log.WriteLine($"Failed to connect -- result was {(int) connectionStatus}");
                    return;
                }
                cancelToken.ThrowIfCancellationRequested();


                // Call the service.
                var response = await appConnection.SendMessageAsync(new ValueSet());
                if (response.Status != AppServiceResponseStatus.Success)
                {
                    Log.WriteLine($"KeepAlive responded with failure {(int) response.Status}");
                }

                cancelToken.ThrowIfCancellationRequested();
            }
        }
    }
}
