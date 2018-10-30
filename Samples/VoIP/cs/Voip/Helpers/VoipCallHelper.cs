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
using System.Threading.Tasks;
using VoipTasks;
using VoipTasks.BackgroundOperations;
using Windows.ApplicationModel.ExtendedExecution;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;

namespace SDKTemplate.Helpers
{
    static class VoipCallHelper
    {
        enum BackgroundCallState
        {
            Inactive,
            Starting,
            Active,
            Ending
        }

        static readonly object stateLock = new object();
        static BackgroundCallState backgroundCallState = BackgroundCallState.Inactive;
        static ExtendedExecutionSession extensionSession;

        static void AssertStateAndTransition(BackgroundCallState assertedCurrentState)
        {
            lock (stateLock)
            {
                AssertState(assertedCurrentState);
                TransitionToNextState();
            }
        }

        static void AssertState(BackgroundCallState assertedCurrentState)
        {
            lock (stateLock)
            {
                if (assertedCurrentState != backgroundCallState)
                {
                    throw new InvalidOperationException();
                }
            }
        }

        static void TransitionToNextState()
        {
            lock (stateLock)
            {
                var current = backgroundCallState;
                var next = current == BackgroundCallState.Ending ? BackgroundCallState.Inactive : (current + 1);
                backgroundCallState = next;
            }
        }

        public static async Task OnEnteredBackground()
        {
            // Though it's more complicated, it looks like we should set up ExtendedExecutionSessions
            // conditionally during OnEnteredBackground (and teardown during OnLeavingBackground),
            // because MSDN indicates that calling Dispose() eagerly has effects on app budget.
            // Conversely, only use ExtendedExecutionSessions when we really need it.
            // https://docs.microsoft.com/en-us/windows/uwp/launch-resume/run-minimized-with-extended-execution#dispose

            lock (stateLock)
            {
                if (backgroundCallState < BackgroundCallState.Starting ||
                    backgroundCallState > BackgroundCallState.Active)
                {
                    // we're NOT beginning or in the middle of a call, bail out
                    return;
                }
            }

            // we are asynchronously starting a call, or in the middle of one.
            var session = new ExtendedExecutionSession
            {
                Reason = ExtendedExecutionReason.Unspecified
            };
            session.Revoked += ExtendedExecutionRevoked;

            var requestResponse = await session.RequestExtensionAsync();
            if (requestResponse == ExtendedExecutionResult.Denied)
            {
                Log.WriteLine("EnteredBackground, Windows denied our request for extended execution");
                session.Dispose();
                return;
            }

            extensionSession = session;
        }

        public static void OnLeavingBackground()
        {
            EndExtendedExecutionIfActive();
        }

        static void EndExtendedExecutionIfActive()
        {
            if (extensionSession != null)
            {
                extensionSession.Revoked -= ExtendedExecutionRevoked;
                extensionSession.Dispose();
                extensionSession = null;
            }
        }

        static void ExtendedExecutionRevoked(object sender, ExtendedExecutionRevokedEventArgs args)
        {
            switch (args.Reason)
            {
                case ExtendedExecutionRevokedReason.Resumed:
                    // Ideally, because we Dispose() during OnLeavingBackground(), you would think
                    // Windows wouldn't need to revoke (cancel) us. But if they do redundantly, oh well
                    // not the end of the world.
                    Log.WriteLine("Extended execution revoked because of resume");
                    break;
                case ExtendedExecutionRevokedReason.SystemPolicy:
                    Log.WriteLine("Extended execution revoked because of System Policy");
                    break;
                default:
                    Log.WriteLine($"Extended execution revoked due to unknown reason (integer value = {(int) args.Reason})");
                    break;
            }

            EndExtendedExecutionIfActive();
        }

        public static async Task<OperationResult> NewOutgoingCallAsync(String contactName, String contactNumber)
        {
            if (!ApiInformation.IsApiContractPresent("Windows.ApplicationModel.Calls.CallsVoipContract", 1))
            {
                return OperationResult.Failed;
            }

            AssertStateAndTransition(BackgroundCallState.Inactive);

            AppServiceHelper appServiceHelper = new AppServiceHelper();

            ValueSet message = new ValueSet();
            message[NewCallArguments.ContactImage.ToString()] = "";
            message[NewCallArguments.ContactName.ToString()] = contactName;
            message[NewCallArguments.ContactNumber.ToString()] = contactNumber;
            message[NewCallArguments.ServiceName.ToString()] = "My First UWP Voip App";
            message[BackgroundOperation.NewBackgroundRequest] = (int)BackgroundRequest.NewOutgoingCall;

            ValueSet response = await appServiceHelper.SendMessageAsync(message);

            TransitionToNextState();

            if (response != null)
            {
                return ((OperationResult)(response[BackgroundOperation.Result]));
            }

            return OperationResult.Failed;
        }

        public static async Task<OperationResult> NewIncomingCallAsync(String contactName, String contactNumber)
        {
            if (!ApiInformation.IsApiContractPresent("Windows.ApplicationModel.Calls.CallsVoipContract", 1))
            {
                return OperationResult.Failed;
            }

            AssertStateAndTransition(BackgroundCallState.Inactive);

            AppServiceHelper appServiceHelper = new AppServiceHelper();

            ValueSet message = new ValueSet();
            message[BackgroundOperation.NewBackgroundRequest] = (int)BackgroundRequest.NewIncomingCall;
            message[NewCallArguments.ContactImage.ToString()] = "";
            message[NewCallArguments.ContactName.ToString()] = contactName;
            message[NewCallArguments.ContactNumber.ToString()] = contactNumber;
            message[NewCallArguments.ServiceName.ToString()] = "My First UWP Voip App";
            
            ValueSet response = await appServiceHelper.SendMessageAsync(message);

            TransitionToNextState();

            if (response != null)
            {
                return ((OperationResult)(response[BackgroundOperation.Result]));
            }

            return OperationResult.Failed;
        }

        public static OperationResult EndCallAsync()
        {
            AssertStateAndTransition(BackgroundCallState.Inactive);

            AppServiceHelper appServiceHelper = new AppServiceHelper();

            ValueSet message = new ValueSet();
            message[BackgroundOperation.NewBackgroundRequest] = (int)BackgroundRequest.EndCall;

            appServiceHelper.SendMessage(message);

            EndExtendedExecutionIfActive();

            TransitionToNextState();

            return OperationResult.Succeeded;
        }

        public static async Task<String> GetCallDurationAsync()
        {
            AssertState(BackgroundCallState.Active);

            AppServiceHelper appServiceHelper = new AppServiceHelper();

            ValueSet message = new ValueSet();
            message[BackgroundOperation.NewBackgroundRequest] = (int)BackgroundRequest.GetCallDuration;

            ValueSet response = await appServiceHelper.SendMessageAsync(message);

            if (response != null)
            {
                return ((response[BackgroundOperation.Result]) as String);
            }

            return new TimeSpan().ToString();
        }

        public static async Task<OperationResult> StartVideoAsync()
        {
            AppServiceHelper appServiceHelper = new AppServiceHelper();

            ValueSet message = new ValueSet();
            message[BackgroundOperation.NewBackgroundRequest] = (int)BackgroundRequest.StartVideo;

            ValueSet response = await appServiceHelper.SendMessageAsync(message);

            if (response != null)
            {
                return ((OperationResult)(response[BackgroundOperation.Result]));
            }

            return OperationResult.Failed;
        }

        public static async Task<OperationResult> StopVideoAsync()
        {
            AppServiceHelper appServiceHelper = new AppServiceHelper();

            ValueSet message = new ValueSet();
            message[BackgroundOperation.NewBackgroundRequest] = (int)BackgroundRequest.StartVideo;

            ValueSet response = await appServiceHelper.SendMessageAsync(message);

            if (response != null)
            {
                return ((OperationResult)(response[BackgroundOperation.Result]));
            }

            return OperationResult.Failed;
        }
    }
}
