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
using VoipTasks.Helpers;
using Windows.ApplicationModel.Background;

namespace VoipTasks
{
    public sealed class CallRtcTask : IBackgroundTask
    {
        BackgroundTaskDeferral _deferral;
        Guid instanceId;

        public void Run(IBackgroundTaskInstance taskInstance)
        {
            _deferral = taskInstance.GetDeferral();
            instanceId = taskInstance.InstanceId;
            Log.WriteLine($"Activating CallRtcTask '{instanceId}'");

            Current.RTCTaskDeferral = _deferral;
            // Register for Task Cancel callback
            taskInstance.Canceled += TaskInstance_Canceled;            
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

            Current.RTCTaskDeferral = null;
        }
    }
}
