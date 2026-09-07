using UnityEngine;

public static class SystemMessageFunc
{
    public static SystemMessage messageComponent;


    public static void Notify(string message, bool isAlert = false)
    {
        if(null == messageComponent)
        {
            string loadPath = "Modules/SystemMessage";
            var obj = Resources.Load<GameObject>(loadPath);
            var parent = GameObject.Find("UICanvas");
            var instance = GameObject.Instantiate(obj, parent.transform);
            messageComponent = instance.GetComponent<SystemMessage>();
        }
        
        messageComponent.Open(new SystemMessage.Message { 
            text = message,
            alert = isAlert
        });
    }

    public static void Debug(string message)
    {
#if UNITY_EDITOR
        string debug = "<color=green>개발용 메시지\n";
        if (null == messageComponent)
        {
            UnityEngine.Debug.Log(message);
            string loadPath = "Modules/SystemMessage";
            var obj = Resources.Load<GameObject>(loadPath);
            var parent = GameObject.Find("UICanvas");
            var instance = GameObject.Instantiate(obj, parent.transform);
            messageComponent = instance.GetComponent<SystemMessage>();
        }
        messageComponent.Open(new SystemMessage.Message
        {
            text = debug + message,
            alert = false
        });
#endif
    }
}
