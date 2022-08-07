using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using AzureSpatialAnchors;
using System.Threading.Tasks;

namespace Scripts
{
    public class PhotonRoom : MonoBehaviourPunCallbacks, IInRoomCallbacks
    {
        public static PhotonRoom Room;

        [SerializeField] private GameObject photonUserPrefab = default;
        [SerializeField] private GameObject longTapSpherePrefab = default;
        [SerializeField] private GameObject roomStatsDisplay = default;

        // private PhotonView pv;
        private Player[] photonPlayers;
        private int playersInRoom;
        private int myNumberInRoom;

        // private GameObject module;
        // private Vector3 moduleLocation = Vector3.zero;

        enum RoomStatus
        {
            None,
            CreatedRoom,
            JoinedRoom,
            JoinedRoomDownloadedAnchor
        }

        public int emptyRoomTimeToLiveSeconds = 120;

        RoomStatus roomStatus = RoomStatus.None;

        static readonly string ANCHOR_ID_CUSTOM_PROPERTY = "anchorId";
        static readonly string ROOM_NAME = "HardCodedRoomName";

        [SerializeField] public GameObject ASAObject;
        private AzureSpatialAnchors.AzureSpatialAnchors ASAScript;

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            base.OnPlayerEnteredRoom(newPlayer);
            photonPlayers = PhotonNetwork.PlayerList;
            playersInRoom++;
            roomStatsDisplay.GetComponent<TextMeshPro>().text = $"# Players in room: {playersInRoom}";
            Debug.Log($"Player, {newPlayer.NickName}, entered room");
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            base.OnPlayerLeftRoom(otherPlayer);
            photonPlayers = PhotonNetwork.PlayerList;
            playersInRoom--;
            roomStatsDisplay.GetComponent<TextMeshPro>().text = $"# Players in room: {playersInRoom}";
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
            ASAScript = ASAObject.GetComponent<AzureSpatialAnchors.AzureSpatialAnchors>();
            // pv = GetComponent<PhotonView>();

            // Allow prefabs not in a Resources folder
            if (PhotonNetwork.PrefabPool is DefaultPool pool)
            {
                if (photonUserPrefab != null) pool.ResourceCache.Add(photonUserPrefab.name, photonUserPrefab);

                if (longTapSpherePrefab != null) pool.ResourceCache.Add(longTapSpherePrefab.name, longTapSpherePrefab);
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

            roomStatsDisplay.GetComponent<TextMeshPro>().text = $"# Players in room: {playersInRoom}";

            // Note that the creator of the room also joins the room...
            if (this.roomStatus == RoomStatus.None)
            {
                this.roomStatus = RoomStatus.JoinedRoom;
            }
            await this.PopulateAnchorAsync();
        }

        private void StartGame()
        {
            CreatPlayer();

            if (!PhotonNetwork.IsMasterClient) return;

            //if (TableAnchor.Instance != null) CreateInteractableObjects();
        }

        private void CreatPlayer()
        {
            var player = PhotonNetwork.Instantiate(photonUserPrefab.name, Vector3.zero, Quaternion.identity);
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

        public override void OnConnectedToMaster()
        {
            base.OnConnectedToMaster();

            var roomOptions = new RoomOptions();
            roomOptions.EmptyRoomTtl = this.emptyRoomTimeToLiveSeconds * 1000;
            PhotonNetwork.JoinOrCreateRoom(ROOM_NAME, roomOptions, null);
        }
    
        public async override void OnCreatedRoom()
        {
            base.OnCreatedRoom();
            this.roomStatus = RoomStatus.CreatedRoom;
            await this.CreateAnchorAsync();
        }

        async Task CreateAnchorAsync()
        {
            // If we created the room then we will attempt to create an anchor for the parent
            // of the cubes that we are creating.
            var anchorService = ASAScript;

            var anchorId = await anchorService.CreateAnchorOnObjectAsync(this.gameObject);

            // Put this ID into a custom property so that other devices joining the
            // room can get hold of it.
#if UNITY_2020 
             PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable()
                {
                    { ANCHOR_ID_CUSTOM_PROPERTY, anchorId }
                }
             );
#endif
        }
        async Task PopulateAnchorAsync()
        {
            if (this.roomStatus == RoomStatus.JoinedRoom)
            {
                object keyValue = null;

#if UNITY_2020
                // First time around, this property may not be here so we see if is there.
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(
                    ANCHOR_ID_CUSTOM_PROPERTY, out keyValue))
                {
                    // If the anchorId property is present then we will try and get the
                    // anchor but only once so change the status.
                    this.roomStatus = RoomStatus.JoinedRoomDownloadedAnchor;

                    // If we didn't create the room then we want to try and get the anchor
                    // from the cloud and apply it.
                    await ASAScript.PopulateAnchorOnObjectAsync(
                        (string)keyValue, this.gameObject);
                }
#endif
            }
        }
        public async override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            base.OnRoomPropertiesUpdate(propertiesThatChanged);

            await this.PopulateAnchorAsync();
        }
    }
}