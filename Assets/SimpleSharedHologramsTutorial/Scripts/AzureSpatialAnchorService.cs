using Microsoft.Azure.SpatialAnchors;
using Microsoft.Azure.SpatialAnchors.Unity;
using Microsoft.MixedReality.Toolkit.Utilities;
using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.WSA;

namespace AzureSpatialAnchors
{
    [RequireComponent(typeof(SpatialAnchorManager))]
    public class AzureSpatialAnchorService : MonoBehaviour
    {
        [Serializable]
        public class AzureSpatialAnchorServiceProfile
        {
            [SerializeField]
            [Tooltip("The account id from the Azure portal for the Azure Spatial Anchors service")]
            string azureAccountId;
            public string AzureAccountId => this.azureAccountId;

            [SerializeField]
            [Tooltip("The access key from the Azure portal for the Azure Spatial Anchors service (for Key authentication)")]
            string azureServiceKey;
            public string AzureServiceKey => this.azureServiceKey;
        }

        [SerializeField]
        [Tooltip("The configuration for the Azure Spatial Anchors Service")]
        AzureSpatialAnchorServiceProfile profile = new AzureSpatialAnchorServiceProfile();
        public AzureSpatialAnchorServiceProfile Profile => this.profile;

        TaskCompletionSource<CloudSpatialAnchor> taskWaitForAnchorLocation;

        //CloudSpatialAnchorSession cloudSpatialAnchorSession;

        private SpatialAnchorManager _spatialAnchorManager = null;

        public AzureSpatialAnchorService()
        {
        }
        public async Task<string> CreateAnchorOnObjectAsync(GameObject gameObjectForAnchor)
        {
            string anchorId = string.Empty;
            try
            {
                await this.StartSession();

                Debug.Log("Started Session");

                //Add and configure ASA components
                CloudNativeAnchor cloudNativeAnchor = gameObjectForAnchor.AddComponent<CloudNativeAnchor>();
                await cloudNativeAnchor.NativeToCloud();
                Debug.Log("After NativeToCloud");
                CloudSpatialAnchor cloudSpatialAnchor = cloudNativeAnchor.CloudAnchor;
                cloudSpatialAnchor.Expiration = DateTimeOffset.Now.AddDays(3);

                // As per previous comment.
                //Collect Environment Data
                while (!_spatialAnchorManager.IsReadyForCreate)
                {
                    float createProgress = _spatialAnchorManager.SessionStatus.RecommendedForCreateProgress;
                    Debug.Log($"ASA - Move your device to capture more environment data: {createProgress:0%}");
                }

                Debug.Log($"ASA - Saving room cloud anchor... ");
                await _spatialAnchorManager.CreateAnchorAsync(cloudSpatialAnchor);

                anchorId = cloudSpatialAnchor?.Identifier;

                bool saveSucceeded = cloudSpatialAnchor != null;
                if (!saveSucceeded)
                {
                    Debug.LogError("ASA - Failed to save, but no exception was thrown.");
                    return anchorId;
                }

                anchorId = cloudSpatialAnchor.Identifier;
                Debug.Log($"ASA - Saved room cloud anchor with ID: {anchorId}");
            }
            catch (Exception exception) // TODO: reasonable exceptions here.
            {
                Debug.Log("ASA - Failed to save room anchor: " + exception.ToString());
                Debug.LogException(exception);
            }
            return (anchorId);
        }
        public async Task<bool> PopulateAnchorOnObjectAsync(string anchorId, GameObject gameObjectForAnchor)
        {
            bool anchorLocated = false;

            try
            {
                await this.StartSession();

                this.taskWaitForAnchorLocation = new TaskCompletionSource<CloudSpatialAnchor>();

                var watcher = _spatialAnchorManager.Session.CreateWatcher(
                    new AnchorLocateCriteria()
                    {
                        Identifiers = new string[] { anchorId },
                        BypassCache = true,
                        Strategy = LocateStrategy.AnyStrategy,
                        RequestedCategories = AnchorDataCategory.Spatial
                    }
                );

                var cloudAnchor = await this.taskWaitForAnchorLocation.Task;

                anchorLocated = cloudAnchor != null;

                if (anchorLocated)
                {
                    Debug.Log("Anchor located");
                    gameObjectForAnchor.AddComponent<CloudNativeAnchor>().CloudToNative(cloudAnchor);
                    Debug.Log("Attached Local Anchor");
                }
                watcher.Stop();
            }
            catch (Exception ex) // TODO: reasonable exceptions here.
            {
                Debug.Log($"Caught {ex.Message}");
            }
            return (anchorLocated);
        }
        /// <summary>
        /// Start the Azure Spatial Anchor Service session
        /// This must be called before calling create, populate or delete methods.
        /// </summary>
        public async Task<bool> StartSession()
        {
            //if (this.cloudSpatialAnchorSession == null)
            //{
            //    Debug.Assert(this.cloudSpatialAnchorSession == null);

            //    this.ThrowOnBadAuthConfiguration();
            //    // setup the session
            //    this.cloudSpatialAnchorSession = new CloudSpatialAnchorSession();
            //    // set the Azure configuration parameters
            //    this.cloudSpatialAnchorSession.Configuration.AccountId = this.Profile.AzureAccountId;
            //    this.cloudSpatialAnchorSession.Configuration.AccountKey = this.Profile.AzureServiceKey;
            //    // register event handlers
            //    this.cloudSpatialAnchorSession.Error += this.OnCloudSessionError;
            //    this.cloudSpatialAnchorSession.AnchorLocated += OnAnchorLocated;
            //    this.cloudSpatialAnchorSession.LocateAnchorsCompleted += OnLocateAnchorsCompleted;

            //    // start the session
            //    this.cloudSpatialAnchorSession.Start();
            //}

            _spatialAnchorManager = GetComponent<SpatialAnchorManager>();
            _spatialAnchorManager.LogDebug += (sender, args) => Debug.Log($"ASA - Debug: {args.Message}");
            _spatialAnchorManager.Error += (sender, args) => Debug.LogError($"ASA - Error: {args.ErrorMessage}");
            _spatialAnchorManager.AnchorLocated += OnAnchorLocated;
            //_spatialAnchorManager.LocateAnchorsCompleted += OnLocateAnchorsCompleted;
            await _spatialAnchorManager.StartSessionAsync();
            return true;
        }
        /// <summary>
        /// Stop the Azure Spatial Anchor Service session
        /// </summary>
        //public void StopSession()
        //{
        //    if (this.cloudSpatialAnchorSession != null)
        //    {
        //        // stop session
        //        this.cloudSpatialAnchorSession.Stop();
        //        // clear event handlers
        //        this.cloudSpatialAnchorSession.Error -= this.OnCloudSessionError;
        //        this.cloudSpatialAnchorSession.AnchorLocated -= OnAnchorLocated;
        //        this.cloudSpatialAnchorSession.LocateAnchorsCompleted -= OnLocateAnchorsCompleted;
        //        // cleanup
        //        this.cloudSpatialAnchorSession.Dispose();
        //        this.cloudSpatialAnchorSession = null;
        //    }
        //}
        void OnLocateAnchorsCompleted(object sender, LocateAnchorsCompletedEventArgs args)
        {
            Debug.Log("On Locate Anchors Completed");
            Debug.Assert(this.taskWaitForAnchorLocation != null);

            if (!this.taskWaitForAnchorLocation.Task.IsCompleted)
            {
                this.taskWaitForAnchorLocation.TrySetResult(null);
            }
        }
        void OnAnchorLocated(object sender, AnchorLocatedEventArgs args)
        {
            Debug.Log($"On Anchor Located, status is {args.Status} anchor is {args.Anchor?.Identifier}, pointer is {args.Anchor?.LocalAnchor}");
            Debug.Assert(this.taskWaitForAnchorLocation != null);

            this.taskWaitForAnchorLocation.SetResult(args.Anchor);
        }
        void OnCloudSessionError(object sender, SessionErrorEventArgs args)
        {
            Debug.Log($"On Cloud Session Error: {args.ErrorMessage}");
        }
        void ThrowOnBadAuthConfiguration()
        {
            if (string.IsNullOrEmpty(this.Profile.AzureAccountId) ||
                string.IsNullOrEmpty(this.Profile.AzureServiceKey))
            {
                throw new ArgumentNullException("Missing required configuration to connect to service");
            }
        }
    }
}