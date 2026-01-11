using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace GorillaTagNametagMod
{
    [BepInPlugin("com.orion.gorillatag.gnt", "GNT", "1.0.5")]
    public class NametagMod : BaseUnityPlugin, IInRoomCallbacks
    {
        private Dictionary<Player, NametagDisplay> nametags = new Dictionary<Player, NametagDisplay>();
        private Dictionary<Player, GameObject> rigCache = new Dictionary<Player, GameObject>();

        private ConfigEntry<bool> showFPS;
        private ConfigEntry<float> maxDistance;
        private ConfigEntry<float> heightOffset;
        private ConfigEntry<bool> debugMode;
        private ConfigEntry<float> nametagScale;

        private float rigSearchCooldown = 0f;

        void Awake()
        {
            showFPS = Config.Bind("General", "ShowFPS", true, "Show FPS above name");
            maxDistance = Config.Bind("General", "MaxDistance", 50f, "Max nametag distance");
            heightOffset = Config.Bind("General", "HeightOffset", 0.3f, "Height above head");
            debugMode = Config.Bind("General", "DebugMode", true, "Enable debug logging");
            nametagScale = Config.Bind("General", "NametagScale", 0.01f, "Nametag scale (try 0.01 to 0.1)");

            PhotonNetwork.AddCallbackTarget(this);
            Log("GNT loaded - Version 1.0.5");
        }

        void OnDestroy()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
            CleanupAllNametags();
        }

        void Update()
        {
            if (!PhotonNetwork.InRoom)
            {
                if (nametags.Count > 0)
                    CleanupAllNametags();
                return;
            }

            rigSearchCooldown -= Time.deltaTime;

            foreach (var player in PhotonNetwork.PlayerList)
            {
                if (player == PhotonNetwork.LocalPlayer) continue;

               
                if (!nametags.ContainsKey(player))
                {
                    CreateNametagForPlayer(player);
                }

                
                if (rigSearchCooldown <= 0f && (!rigCache.ContainsKey(player) || rigCache[player] == null))
                {
                    var foundRig = FindVRRig(player);
                    if (foundRig != null)
                    {
                        rigCache[player] = foundRig;
                        Log($"Found rig for {player.NickName}");
                    }
                }

                
                if (nametags.ContainsKey(player) && nametags[player] != null)
                {
                    if (rigCache.ContainsKey(player) && rigCache[player] != null)
                    {
                        Transform head = FindHead(rigCache[player]);
                        Vector3 headPos = head.position + Vector3.up * heightOffset.Value;
                        nametags[player].SetPosition(headPos);
                        nametags[player].SetVisibility(maxDistance.Value);
                    }
                    else
                    {
                        
                        nametags[player].gameObject.SetActive(false);
                    }
                }
            }

            if (rigSearchCooldown <= 0f)
                rigSearchCooldown = 1f; 

            CleanupLeftPlayers();
        }

        private Transform FindHead(GameObject rig)
        {
            
            string[] headNames = { "head", "Head", "HEAD", "rig/head", "body/head" };

            foreach (string headName in headNames)
            {
                Transform head = rig.transform.Find(headName);
                if (head != null) return head;
            }

            
            Transform foundHead = rig.transform.FindDeepChild("head");
            if (foundHead != null) return foundHead;

            
            return rig.transform;
        }

        private void CreateNametagForPlayer(Player player)
        {
            try
            {
                GameObject go = new GameObject("Nametag_" + player.NickName);
                DontDestroyOnLoad(go);

                var display = go.AddComponent<NametagDisplay>();
                display.Init(player, this, nametagScale.Value);
                nametags[player] = display;

                Log($"Created nametag for {player.NickName} (Actor #{player.ActorNumber})");
            }
            catch (Exception e)
            {
                LogError($"Failed to create nametag for {player.NickName}: {e.Message}\n{e.StackTrace}");
            }
        }

        private void CleanupLeftPlayers()
        {
            var playersToRemove = nametags.Keys.Where(p => !PhotonNetwork.PlayerList.Contains(p)).ToList();
            foreach (var p in playersToRemove)
            {
                if (nametags[p] != null)
                    Destroy(nametags[p].gameObject);
                nametags.Remove(p);
                rigCache.Remove(p);
                Log($"Removed nametag for {p.NickName}");
            }
        }

        private void CleanupAllNametags()
        {
            foreach (var display in nametags.Values)
            {
                if (display != null)
                    Destroy(display.gameObject);
            }
            nametags.Clear();
            rigCache.Clear();
            Log("Cleaned up all nametags");
        }

        private GameObject FindVRRig(Player player)
        {
            try
            {
             
                PhotonView[] photonViews = FindObjectsOfType<PhotonView>();
                foreach (var pv in photonViews)
                {
                    if (pv.Owner != null && pv.Owner.ActorNumber == player.ActorNumber)
                    {
                        
                        GameObject obj = pv.gameObject;
                        if (obj.name.ToLower().Contains("rig") ||
                            obj.name.ToLower().Contains("gorilla") ||
                            obj.name.ToLower().Contains("player"))
                        {
                            Log($"Found rig via PhotonView for {player.NickName}: {obj.name}");
                            return obj;
                        }

                        
                        if (obj.transform.parent != null)
                        {
                            GameObject parent = obj.transform.parent.gameObject;
                            if (parent.name.ToLower().Contains("rig") ||
                                parent.name.ToLower().Contains("gorilla"))
                            {
                                Log($"Found rig via PhotonView parent for {player.NickName}: {parent.name}");
                                return parent;
                            }
                        }

                        
                        if (obj.transform.Find("head") != null || obj.transform.Find("Head") != null)
                        {
                            Log($"Found rig via head child for {player.NickName}: {obj.name}");
                            return obj;
                        }
                    }
                }

                
                var allComponents = FindObjectsOfType<MonoBehaviour>();
                foreach (var component in allComponents)
                {
                    if (component == null) continue;

                    string typeName = component.GetType().Name;
                    if (typeName == "VRRig")
                    {
                        
                        object owner = GetMember(component, "creator")
                                    ?? GetMember(component, "owner")
                                    ?? GetMember(component, "Creator")
                                    ?? GetMember(component, "photonView");

                        if (owner is Player p && p.ActorNumber == player.ActorNumber)
                        {
                            Log($"Found VRRig component for {player.NickName}");
                            return component.gameObject;
                        }

                        
                        var pv = component.GetComponent<PhotonView>();
                        if (pv != null && pv.Owner != null && pv.Owner.ActorNumber == player.ActorNumber)
                        {
                            Log($"Found VRRig with PhotonView for {player.NickName}");
                            return component.gameObject;
                        }
                    }
                }

                
                GameObject[] allObjects = FindObjectsOfType<GameObject>();
                foreach (GameObject obj in allObjects)
                {
                    if (obj.name.ToLower().Contains("rig") || obj.name.ToLower().Contains("gorilla"))
                    {
                        var pv = obj.GetComponent<PhotonView>() ?? obj.GetComponentInParent<PhotonView>();
                        if (pv != null && pv.Owner != null && pv.Owner.ActorNumber == player.ActorNumber)
                        {
                            Log($"Found rig by name search for {player.NickName}: {obj.name}");
                            return obj;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                LogError($"Error finding VRRig for {player.NickName}: {e.Message}");
            }

            return null;
        }

        private static object GetMember(object obj, string name)
        {
            if (obj == null) return null;
            try
            {
                var t = obj.GetType();
                var f = t.GetField(name, System.Reflection.BindingFlags.Instance |
                                          System.Reflection.BindingFlags.Public |
                                          System.Reflection.BindingFlags.NonPublic);
                if (f != null) return f.GetValue(obj);

                var p = t.GetProperty(name, System.Reflection.BindingFlags.Instance |
                                            System.Reflection.BindingFlags.Public |
                                            System.Reflection.BindingFlags.NonPublic);
                if (p != null) return p.GetValue(obj, null);
            }
            catch { }

            return null;
        }

        public bool ShowFPS() => showFPS.Value;

        private void Log(string msg)
        {
            if (debugMode.Value)
                Logger.LogInfo(msg);
        }

        private void LogError(string msg)
        {
            Logger.LogError(msg);
        }

        
        public void OnPlayerEnteredRoom(Player newPlayer)
        {
            Log($"Player entered: {newPlayer.NickName} (Actor #{newPlayer.ActorNumber})");
        }

        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            Log($"Player left: {otherPlayer.NickName}");
            if (nametags.ContainsKey(otherPlayer))
            {
                Destroy(nametags[otherPlayer].gameObject);
                nametags.Remove(otherPlayer);
            }
            if (rigCache.ContainsKey(otherPlayer))
                rigCache.Remove(otherPlayer);
        }

        public void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged) { }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps) { }
        public void OnMasterClientSwitched(Player newMasterClient) { }
    }

    public class NametagDisplay : MonoBehaviour
    {
        private Player player;
        private NametagMod mod;
        private TextMesh textMesh;
        private GameObject textObject;
        private MeshRenderer meshRenderer;

        private float timer;
        private int frames;
        private float fps;
        private bool initialized = false;

        public void Init(Player p, NametagMod m, float scale)
        {
            player = p;
            mod = m;

            try
            {
                
                textObject = new GameObject("TextMesh");
                textObject.transform.SetParent(transform, false);
                textObject.transform.localPosition = Vector3.zero;
                textObject.transform.localRotation = Quaternion.identity;
                textObject.transform.localScale = Vector3.one;

                
                textObject.layer = 0;
                gameObject.layer = 0;

                textMesh = textObject.AddComponent<TextMesh>();

                
                textMesh.text = p.NickName;
                textMesh.fontSize = 200;
                textMesh.characterSize = 0.05f;
                textMesh.anchor = TextAnchor.MiddleCenter;
                textMesh.alignment = TextAlignment.Center;
                textMesh.color = Color.white;
                textMesh.richText = false;

                
                Font arialFont = (Font)Resources.GetBuiltinResource(typeof(Font), "Arial.ttf");
                if (arialFont != null)
                {
                    textMesh.font = arialFont;
                }
                else
                {
                    
                    Font[] fonts = Resources.FindObjectsOfTypeAll<Font>();
                    if (fonts != null && fonts.Length > 0)
                    {
                        textMesh.font = fonts[0];
                    }
                }

                
                meshRenderer = textMesh.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    
                    Shader shader = Shader.Find("GUI/Text Shader");
                    if (shader == null) shader = Shader.Find("UI/Default");
                    if (shader == null) shader = Shader.Find("Unlit/Transparent");
                    if (shader == null) shader = Shader.Find("Unlit/Color");
                    if (shader == null) shader = Shader.Find("Legacy Shaders/Diffuse");

                    if (shader != null)
                    {
                        Material mat = new Material(shader);
                        mat.color = Color.white;

                        
                        mat.renderQueue = 3000;

                        if (textMesh.font != null && textMesh.font.material != null)
                        {
                            mat.mainTexture = textMesh.font.material.mainTexture;
                        }

                        meshRenderer.material = mat;
                    }
                    else if (textMesh.font != null && textMesh.font.material != null)
                    {
                        
                        meshRenderer.material = new Material(textMesh.font.material);
                        meshRenderer.material.color = Color.white;
                    }

                    
                    meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = false;
                    meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                }

                
                transform.localScale = Vector3.one * scale;

                initialized = true;
                UpdateText();

                
                gameObject.SetActive(true);
                textObject.SetActive(true);

                Debug.Log($"[GNT] Nametag initialized for {p.NickName} - Layer: {gameObject.layer}, Renderer enabled: {meshRenderer.enabled}, Material: {meshRenderer.material?.shader?.name}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GNT] NametagDisplay Init failed for {p.NickName}: {e.Message}\n{e.StackTrace}");
            }
        }

        void Update()
        {
            if (!initialized || player == null || textMesh == null) return;

            
            frames++;
            timer += Time.deltaTime;

            if (timer >= 0.5f)
            {
                fps = frames / timer;
                frames = 0;
                timer = 0f;
                UpdateText();
            }

            
            if (Camera.main != null)
            {
                
                transform.rotation = Camera.main.transform.rotation;
            }
        }

        void UpdateText()
        {
            if (textMesh == null || player == null) return;

            try
            {
                string displayText = player.NickName;

                if (mod != null && mod.ShowFPS())
                {
                    displayText += "\n" + Mathf.RoundToInt(fps) + " FPS";
                }

                textMesh.text = displayText;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GNT] UpdateText failed: {e.Message}");
            }
        }

        public void SetPosition(Vector3 pos)
        {
            if (transform != null)
                transform.position = pos;
        }

        public void SetVisibility(float maxDist)
        {
            if (!initialized || Camera.main == null) return;

            try
            {
                float distance = Vector3.Distance(Camera.main.transform.position, transform.position);
                bool shouldBeActive = distance <= maxDist;

                if (gameObject.activeSelf != shouldBeActive)
                {
                    gameObject.SetActive(shouldBeActive);
                    if (textObject != null)
                        textObject.SetActive(shouldBeActive);
                }
            }
            catch { }
        }

        void OnDrawGizmos()
        {
            
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.1f);
        }
    }

    
    public static class TransformExtensions
    {
        public static Transform FindDeepChild(this Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name.ToLower().Contains(name.ToLower()))
                    return child;

                Transform result = child.FindDeepChild(name);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
}