/// TCPClient for HL2
/// 1. https://medium.datadriveninvestor.com/connecting-the-microsoft-hololens-and-raspberry-pi3b-58665032964c
/// 2. https://stackoverflow.com/questions/46964007/python-socket-connection-between-raspberry-pi-s
/// 3. https://stackoverflow.com/questions/56194446/send-big-file-over-socket
/// 4. https://nikhilroxtomar.medium.com/file-transfer-using-tcp-socket-in-python3-idiot-developer-c5cf3899819c

using Microsoft.MixedReality.Toolkit.Experimental.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Scripts
{
    public class TCPClient : MonoBehaviour
    {
        [SerializeField] private GameObject holdsParent;

        // server ip address and port
        [SerializeField] private string host = "10.203.94.234";
        [SerializeField] private string port = "8081";

        // filename keyboard/input related fields
        [SerializeField] private InputField keyboardInput = default;
        [SerializeField] private GameObject keyboardInputContainer = default;
        private string filename = default;

        // scroll menu for config choices
        [SerializeField] private ScrollHoldConfigMenuPopulator scrollHoldConfigMenuScript = default;

#if !UNITY_EDITOR
    private bool _useUWP = true;
    private Windows.Networking.Sockets.StreamSocket socket;
#endif

#if UNITY_EDITOR
        private bool _useUWP = false;
        System.Net.Sockets.TcpClient client;
        System.Net.Sockets.NetworkStream stream;
#endif

        private StreamWriter writer;
        private StreamReader reader;

        private int BUFFER_SIZE = 1024;

        private void Start()
        {
            // hide filename prompt until we want it shown
            keyboardInputContainer.SetActive(false);
        }

        public void Connect()
        {
            if (_useUWP)
            {
                ConnectUWP(host, port);
            }
            else
            {
                ConnectUnity(host, port);
            }
        }

#if UNITY_EDITOR
        private void ConnectUWP(string host, string port)
#else
    private async void ConnectUWP(string host, string port)
#endif
        {
#if UNITY_EDITOR
            errorStatus = "UWP TCP client used in Unity!";
#else
        try
        {
            socket = new Windows.Networking.Sockets.StreamSocket();
            Windows.Networking.HostName serverHost = new Windows.Networking.HostName(host);
            await socket.ConnectAsync(serverHost, port);

            Stream streamOut = socket.OutputStream.AsStreamForWrite();
            writer = new StreamWriter(streamOut) { AutoFlush = true };

            Stream streamIn = socket.InputStream.AsStreamForRead();
            reader = new StreamReader(streamIn);

            successStatus = "Connected!";
        }
        catch (Exception e)
        {
            errorStatus = e.ToString();
        }
#endif
        }

        private void ConnectUnity(string host, string port)
        {
#if !UNITY_EDITOR
        errorStatus = "Unity TCP client used in UWP!";
#else
            try
            {
                client = new System.Net.Sockets.TcpClient(host, Int32.Parse(port));
                stream = client.GetStream();
                reader = new StreamReader(stream);
                writer = new StreamWriter(stream) { AutoFlush = true };

                successStatus = "Connected!";
            }
            catch (Exception e)
            {
                errorStatus = e.ToString();
            }
#endif
        }

        private string errorStatus = null;
        private string successStatus = null;

        public void Update()
        {
            if (errorStatus != null)
            {
                Debug.Log(errorStatus);
                errorStatus = null;
            }
            if (successStatus != null)
            {
                Debug.Log(successStatus);
                successStatus = null;
            }
        }

        /// <summary>
        /// Sends a file to server
        /// NOTE: must be async because we are using threading: https://stackoverflow.com/questions/45717656/losing-variable-after-async-call.  
        /// Once a synchronous function sees an await, it will turn over control and when it resumes,
        /// a new thread with a new context will take over after the await.  Thus, object references will be null after the await if using a synchronous call.
        /// NOTE: the reader must be async as otherwise it hangs the main thread in unity and the app exits when trying to perform Read()
        /// </summary>
        /// <param name="filename"></param>
        /// <returns></returns>

        public async Task SendFile(string filename = "")
        {
            try
            {
                //Debug.Log($"filename: {filename}");

                char[] response = new char[BUFFER_SIZE];

                // notify server of endpoint to use
                writer.Write("receiveFile");
                //Debug.Log("written");

                // get receipt confirmation
                await reader.ReadAsync(response, 0, BUFFER_SIZE);
                string responseStr = new string(response);
                responseStr = responseStr.Trim(new Char[] { '\0' }); // trim any empty bytes in the buffer
                if (responseStr.Length <= 0 || !responseStr.Equals("ready")) { return; }

                //Debug.Log($"response: {responseStr}");
                //Debug.Log(writer);
                //Debug.Log("sending filename...");

                // send filename
                writer.Write(filename);

                // get receipt confirmation
                await reader.ReadAsync(response, 0, BUFFER_SIZE);
                responseStr = new string(response);
                responseStr = responseStr.Trim(new Char[] { '\0' }); // trim any empty bytes in the buffer
                if (responseStr.Length <= 0 || !responseStr.Equals("ready")) { return; }

                //Debug.Log($"response: {responseStr}");

                //Debug.Log("sending file contents...");

                // send file contents
                string path = Path.Combine(Application.persistentDataPath, filename);
                using (TextReader sr = File.OpenText(path))
                {
                    string s = "";
                    while ((s = sr.ReadLine()) != null)
                    {
                        // send line of file to server
                        //Debug.Log($"Tx: {s}");
                        writer.Write(s + "\n");

                        // get receipt confirmation
                        await reader.ReadAsync(response, 0, BUFFER_SIZE);
                        responseStr = new string(response);
                        responseStr = responseStr.Trim(new Char[] { '\0' }); // trim any empty bytes in the buffer
                        if (responseStr.Length <= 0 || !responseStr.Equals("ready")) { return; }

                        //Debug.Log($"response: {responseStr}");
                    }

                    // signal done to server
                    //Debug.Log("Tx: Finished");
                    writer.Write("done");
                }
            }
            catch (Exception e)
            {
                Debug.Log(e.ToString());
            }
        }

        public void CloseConnection()
        {
#if UNITY_EDITOR
            stream.Close();
            client.Close();
            writer.Close();
            reader.Close();

            stream = null;
#else
        socket.Dispose();
        writer.Dispose();
        reader.Dispose();

        socket = null;
#endif
            writer = null;
            reader = null;
        }

        /// <summary>
        /// Perform cleanup
        /// NOTE: OnApplicationFocus used because OnDestroy are not reliabe on HL2 headset and also because OnApplicatoinQuit and OnDisable don't appear to
        /// be called when the app is exited by user on HL2.  Also, not clear if this function is called on app crashes.  However, OnApplicationFocus gets
        /// called if click out of play screen in Unity Play mode, which is annoying.  So we use OnDestroy when running on Unity Play mode.
        /// https://stackoverflow.com/questions/28647118/detect-application-quit-with-unity
        /// </summary>
#if UNITY_EDITOR
        private void OnDestroy()
        {
            writer.Write("closeSock");
            CloseConnection();
        }
#else
    void OnApplicationFocus(bool focus)
    {
        if (!focus)
        {
            Debug.Log("OnApplicationFocus");
            writer.Write("closeSock");
            CloseConnection();
        }
    }
#endif

        /// <summary>
        /// Registered in Inspector.
        /// NOTE: need async void here: https://stackoverflow.com/questions/28601678/calling-async-method-on-button-click
        /// </summary>
        public async void SaveHolds()
        {
            CreateFile(filename: filename);

            // send data
            await SendFile(filename: filename);

            // remove the file so we don't accrue files
            DeleteFile(filename: filename);
        }

        /// <summary>
        /// Retrieves the list of saved hold configurations currently on the server (list of filenames)
        /// TODO: maybe use serialization/deserialization and/or JSON?
        /// </summary>
        public async void GetHoldConfigurations()
        {
            List<string> configList = new List<string>();

            // notify server of endpoint
            writer.Write("listFiles");

            // get list of filenames
            while (true)
            {
                char[] response = new char[BUFFER_SIZE];
                await reader.ReadAsync(response, 0, BUFFER_SIZE);
                string responseStr = new string(response);
                responseStr = responseStr.Trim(new Char[] { '\0' }); // trim any empty bytes in the buffer
                if (responseStr.Length <= 0 || responseStr.Equals("done")) { break; }
                configList.Add(responseStr);

                // send ready signal to server to get next file (if there is one)
                writer.Write("ready");
            }

            // print what we got
            for (int i = 0; i < configList.Count; i++)
            {
                Debug.Log($"retreived file: {configList[i]}");
            }

            // populate scroll hold config menu
            scrollHoldConfigMenuScript.NumItems = configList.Count;
            scrollHoldConfigMenuScript.MakeScrollingList(configList);
        }

        /// <summary>
        /// Creates file with specified filename
        /// </summary>
        /// <param name="filename"></param>
        private void CreateFile(string filename = "")
        {
            string path = Path.Combine(Application.persistentDataPath, filename);
            using (StreamWriter sw = File.CreateText(path))
            {
                sw.WriteLine($"Time: {Time.time}");
                sw.WriteLine("Hello World");
            }
        }

        /// <summary>
        /// Deletes file with specified filename
        /// Assumes file exists at Application.persistentDataPath
        /// </summary>
        /// <param name="filename"></param>
        private void DeleteFile(string filename = "")
        {
            string path = Path.Combine(Application.persistentDataPath, filename);
            File.Delete(path);
        }

        /// <summary>
        /// Show container holding input field that triggers keyboard
        /// </summary>
        public void ShowKeyboardInput()
        {
            keyboardInputContainer.SetActive(true);
            keyboardInput.text = "";
        }

        /// <summary>
        /// Called by closing keyboard in Inspector to retrieve the user's text
        /// </summary>
        /// <param name="text"></param>
        public void GetFilename(string text)
        {
            filename = text + ".txt";
            SaveHolds();
            keyboardInputContainer.SetActive(false);
        }

        // <ScrollHoldConfigsMenuClick>
        /// <summary>
        /// Handle scoll hold config menu selection
        /// </summary>
        /// <param name="go"></param>
        public void ScrollHoldConfigsMenuClick(GameObject go)
        {
            Debug.Log(go);
            if (go != null)
            {
                // PhotonNetwork.PrefabPool lets us refer to prefabs by name under Resources folder without having to manually add them to the ResourceCache: https://forum.unity.com/threads/solved-photon-instantiating-prefabs-without-putting-them-in-a-resources-folder.293853/
                string holdConfig = $"{go.name}";
                Debug.Log($"holdConfig: {holdConfig}");
            }
        }
    }
}