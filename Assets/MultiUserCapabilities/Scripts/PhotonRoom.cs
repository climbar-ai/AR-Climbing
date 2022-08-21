using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using AzureSpatialAnchors;
using System.Threading.Tasks;
using Scripts.WorldLocking;
using Microsoft.MixedReality.WorldLocking.ASA;

namespace Scripts
{
    using CloudAnchorId = System.String;

    public class PhotonRoom : MonoBehaviourPunCallbacks, IInRoomCallbacks
    {
        public static PhotonRoom Room;

        [SerializeField] private GameObject photonUserPrefab = default; 
        [SerializeField] private GameObject sharedCursorFocus = default;
        [SerializeField] private GameObject longTapSpherePrefab = default;
        [SerializeField] private GameObject numPlayersDisplay = default;
        [SerializeField] private GameObject publisherStatusDisplay = default;
        [SerializeField] private GameObject roomStatusDisplay = default;
        [SerializeField] private Scripts.WorldLocking.SpacePinBinder spacePinBinder = default;
        
        /// <summary>
        /// Progress indicator object for publisher status
        /// Tells us when we can publish/download spacepin
        /// </summary>
        [SerializeField]
        private GameObject indicatorObject;

        private IBinder binder;

        // private PhotonView pv;
        private Player[] photonPlayers;
        private int playersInRoom;
        private int myNumberInRoom;
        private ActionPublish actionPublish;
        private CloudAnchorId cloudAnchorId = null;

        // private GameObject module;
        // private Vector3 moduleLocation = Vector3.zero;

        enum RoomStatus
        {
            None,
            CreatedRoom,
            CreatedRoomAndPublishedAnchor,
            JoinedRoom,
            JoinedRoomDownloadedAnchor,
            JoinedRoomDownloadedAnchorId,
            JoinedRoomDownloadingAnchor
        }

        public int emptyRoomTimeToLiveSeconds = 120;

        RoomStatus roomStatus = RoomStatus.None;

        public static readonly string CLOUD_ANCHOR_ID_CUSTOM_PROPERTY = "cloudAnchorId";
        static readonly string ROOM_NAME = "HardCodedRoomName";

        //[SerializeField] public GameObject ASAObject;
        //private AzureSpatialAnchors.AzureSpatialAnchors ASAScript;

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            base.OnPlayerEnteredRoom(newPlayer);
            photonPlayers = PhotonNetwork.PlayerList;
            playersInRoom++;
            numPlayersDisplay.GetComponent<TextMeshPro>().text = $"# Players in room: {playersInRoom}";
            Debug.Log($"Player, {newPlayer.NickName}, entered room");
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            base.OnPlayerLeftRoom(otherPlayer);
            photonPlayers = PhotonNetwork.PlayerList;
            playersInRoom--;
            numPlayersDisplay.GetComponent<TextMeshPro>().text = $"# Players in room: {playersInRoom}";
            Debug.Log($"Player, {otherPlayer.NickName}, left room");
        }

        private void Awake()
        {
            if (Room == null)
            {
                Room = this;
            }
            else
            {
                if (Room != this)
                {
                    Destroy(Room.gameObject);
                    Room = this;
                }
            }
        }

        public override void OnEnable()
        {
            base.OnEnable();
            PhotonNetwork.AddCallbackTarget(this);
        }

        public override void OnDisable()
        {
            base.OnDisable();
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        private void Start()
        {
            //ASAScript = ASAObject.GetComponent<AzureSpatialAnchors.AzureSpatialAnchors>();
            // pv = GetComponent<PhotonView>();

            // Allow prefabs not in a Resources folder
            if (PhotonNetwork.PrefabPool is DefaultPool pool)
            {
                if (photonUserPrefab != null) pool.ResourceCache.Add(photonUserPrefab.name, photonUserPrefab);

                if (longTapSpherePrefab != null) pool.ResourceCache.Add(longTapSpherePrefab.name, longTapSpherePrefab);
            }

            binder = spacePinBinder;
            actionPublish = GetComponent<ActionPublish>();
            indicatorObject.SetActive(true);
        }

        private async void Update()
        {
            var status = new ReadinessStatus();
            if (binder != null)
            {
                status = binder.PublisherStatus;
            }
            publisherStatusDisplay.GetComponent<TextMeshPro>().text = $"Publisher Status: {status.readiness}";

            if (status.readiness == PublisherReadiness.Ready)
            {
                indicatorObject.SetActive(false);

                if (roomStatus == RoomStatus.CreatedRoom)
                {
                    // publish spacepin to share common origin
                    actionPublish.DoPublish();
                    roomStatus = RoomStatus.CreatedRoomAndPublishedAnchor;
                    roomStatusDisplay.GetComponent<TextMeshPro>().text = $"Room Status: {roomStatus}";
                } else if (roomStatus == RoomStatus.JoinedRoomDownloadedAnchorId)
                {
                    // cloudAnchorId will be set in room properties on changed callback
                    if (cloudAnchorId != null)
                    {
                        // change status so we don't keep firing calls to download the anchor during the Delay() below
                        roomStatus = RoomStatus.JoinedRoomDownloadingAnchor;
                        roomStatusDisplay.GetComponent<TextMeshPro>().text = $"Room Status: {roomStatus}";

                        // arbitrary delay that is supposed to be long enough for anchorLocateCriteria within publisher to become setup (its setup is called in
                        // SpacePinBinder's Awake())
                        // Ideally this dealy would be replaced by better readiness checks within the publisher itself but I ran into issues trying to copy/modify the PublisherASA file
                        // due to having been compiled in assembly that defines scripting symbols used throughout the PublisherASA code within directives 
                        await Task.Delay(10000);
                        actionPublish.DoDownloadOne(cloudAnchorId);
                        roomStatus = RoomStatus.JoinedRoomDownloadedAnchor;
                        roomStatusDisplay.GetComponent<TextMeshPro>().text = $"Room Status: {roomStatus}";
                    }  
                }
            }
        }

        public async override void OnJoinedRoom()
        {
            base.OnJoinedRoom();

            photonPlayers = PhotonNetwork.PlayerList;
            playersInRoom = photonPlayers.Length;
            myNumberInRoom = playersInRoom;
            PhotonNetwork.NickName = myNumberInRoom.ToString();

            StartGame();

            numPlayersDisplay.GetComponent<TextMeshPro>().text = $"# Players in room: {playersInRoom}";

            // Note that the creator of the room also joins the room...
            if (this.roomStatus == RoomStatus.None)
            {
                this.roomStatus = RoomStatus.JoinedRoom;
                this.roomStatusDisplay.GetComponent<TextMeshPro>().text = $"Room Status: {this.roomStatus}";

                // check to see if cloudAnchorId is present i.e. we joined after the room creator created the cloundAnchorId and triggered OnRoomPropertiesUpdate
                object keyValue = null;

#if UNITY_2020
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                    CLOUD_ANCHOR_ID_CUSTOM_PROPERTY, out keyValue))
                {
                    // If the anchorId property is present then we will try and get the
                    // anchor but only once so change the status.
                    this.cloudAnchorId = (CloudAnchorId)keyValue;
                    this.roomStatus = RoomStatus.JoinedRoomDownloadedAnchorId;
                    this.roomStatusDisplay.GetComponent<TextMeshPro>().text = $"Room Status: {this.roomStatus}";

                    Debug.Log($"OnRoomPropertiesUpdate -> {keyValue}");

                    // If we didn't create the room then we want to try and get the anchor
                    // from the cloud and apply it.
                    //await ASAScript.PopulateAnchorOnObjectAsync(
                    //    (string)keyValue, this.gameObject);
                }
#endif
            }
            //await this.PopulateAnchorAsync();
        }

        private void StartGame()
        {
            CreatPlayer();
            CreateSharedCursorFocus();

            if (!PhotonNetwork.IsMasterClient) return;

            //if (TableAnchor.Instance != null) CreateInteractableObjects();
        }

        private void CreatPlayer()
        {
            var player = PhotonNetwork.Instantiate(photonUserPrefab.name, Vector3.zero, Quaternion.identity);
        }

        private void CreateSharedCursorFocus()
        {
            var cursor = PhotonNetwork.Instantiate(sharedCursorFocus.name, Vector3.zero, Quaternion.Euler(0f, 180f, 0f)); // instantiate for all to see
            int layer = LayerMask.NameToLayer("Ignore Locally"); // hide for this user so we don't have two cursors
            cursor.layer = layer;
            cursor.transform.GetChild(0).gameObject.layer = layer; // setting layer only works at top level of object so we must also call it on the child object since it contains the visible mesh
        }

        //private void CreateInteractableObjects()
        //{
        //    var position = roverExplorerLocation.position;
        //    var positionOnTopOfSurface = new Vector3(position.x, position.y + roverExplorerLocation.localScale.y / 2,
        //        position.z);

        //    var go = PhotonNetwork.Instantiate(roverExplorerPrefab.name, positionOnTopOfSurface,
        //        roverExplorerLocation.rotation);
        //}

        // private void CreateMainLunarModule()
        // {
        //     module = PhotonNetwork.Instantiate(roverExplorerPrefab.name, Vector3.zero, Quaternion.identity);
        //     pv.RPC("Rpc_SetModuleParent", RpcTarget.AllBuffered);
        // }
        //
        // [PunRPC]
        // private void Rpc_SetModuleParent()
        // {
        //     Debug.Log("Rpc_SetModuleParent- RPC Called");
        //     module.transform.parent = TableAnchor.Instance.transform;
        //     module.transform.localPosition = moduleLocation;
        //
        // 
        //void Start()
        //{
        //    PhotonNetwork.ConnectUsingSettings();
        //}

        //        public override void OnConnectedToMaster()
        //        {
        //            base.OnConnectedToMaster();

        //            var roomOptions = new RoomOptions();
        //            roomOptions.EmptyRoomTtl = this.emptyRoomTimeToLiveSeconds * 1000;
        //            PhotonNetwork.JoinOrCreateRoom(ROOM_NAME, roomOptions, null);
        //        }

        public async override void OnCreatedRoom()
        {
            base.OnCreatedRoom();
            this.roomStatus = RoomStatus.CreatedRoom;
            this.roomStatusDisplay.GetComponent<TextMeshPro>().text = $"Room Status: {this.roomStatus}";
            // publish spacepin to share common origin
            //await this.CreateAnchorAsync();
        }

        //        async Task CreateAnchorAsync()
        //        {
        //            // If we created the room then we will attempt to create an anchor for the parent
        //            // of the cubes that we are creating.
        //            var anchorService = ASAScript;

        //            var anchorId = await anchorService.CreateAnchorOnObjectAsync(this.gameObject);

        //            // Put this ID into a custom property so that other devices joining the
        //            // room can get hold of it.
        //#if UNITY_2020 
        //             PhotonNetwork.CurrentRoom.SetCustomProperties(
        //                new Hashtable()
        //                {
        //                    { ANCHOR_ID_CUSTOM_PROPERTY, anchorId }
        //                }
        //             );
        //#endif
        //        }
        //        async Task PopulateAnchorAsync()
        //        {
        //            if (this.roomStatus == RoomStatus.JoinedRoom)
        //            {
        //                object keyValue = null;

        //#if UNITY_2020
        //                // First time around, this property may not be here so we see if is there.
        //                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
        //                    ANCHOR_ID_CUSTOM_PROPERTY, out keyValue))
        //                {
        //                    // If the anchorId property is present then we will try and get the
        //                    // anchor but only once so change the status.
        //                    this.roomStatus = RoomStatus.JoinedRoomDownloadedAnchor;

        //                    // If we didn't create the room then we want to try and get the anchor
        //                    // from the cloud and apply it.
        //                    await ASAScript.PopulateAnchorOnObjectAsync(
        //                        (string)keyValue, this.gameObject);
        //                }
        //#endif
        //            }
        //        }

        public async override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            base.OnRoomPropertiesUpdate(propertiesThatChanged);
            
            // room creator can skip this
            if (this.roomStatus == RoomStatus.JoinedRoom)
            {
                object keyValue = null;

#if UNITY_2020
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                    CLOUD_ANCHOR_ID_CUSTOM_PROPERTY, out keyValue))
                {
                    // If the anchorId property is present then we will try and get the
                    // anchor but only once so change the status.
                    this.cloudAnchorId = (CloudAnchorId)keyValue;
                    this.roomStatus = RoomStatus.JoinedRoomDownloadedAnchorId;
                    this.roomStatusDisplay.GetComponent<TextMeshPro>().text = $"Room Status: {this.roomStatus}";

                    Debug.Log($"OnRoomPropertiesUpdate -> {keyValue}");

                    // If we didn't create the room then we want to try and get the anchor
                    // from the cloud and apply it.
                    //await ASAScript.PopulateAnchorOnObjectAsync(
                    //    (string)keyValue, this.gameObject);
                }
#endif

                //await this.PopulateAnchorAsync();
            }
        }
    }
}