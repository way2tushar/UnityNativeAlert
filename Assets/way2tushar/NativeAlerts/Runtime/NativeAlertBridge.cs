// Assets/way2tushar/NativeAlerts/Runtime/NativeAlertBridge.cs
using UnityEngine;

namespace way2tushar.NativeAlerts
{
    public class NativeAlertBridge : MonoBehaviour
    {
        const string GoName = "NativeAlertBridge_GO";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (GameObject.Find(GoName) != null) return;
            var go = new GameObject(GoName);
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            go.AddComponent<NativeAlertBridge>();
        }

        // iOS delivers "<id>|<index>" via UnitySendMessage
        public void OnAlertResult(string payload)
        {
            NativeAlert.Resolve(payload);
        }
    }
}
