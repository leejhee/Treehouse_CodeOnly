using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SystemMessage : MonoBehaviour
{
    public struct Message
    {
        public string text;
        public bool alert;
    }
    public TMP_Text messageText;
    public Image bg;


    [HideInInspector]
    private bool CorRunning = false;
    public void Open(Message message)
    {
        if(true == CorRunning)
        {
            StopAllCoroutines();
            CorRunning = false;
        }

        messageText.SetText(message.text);
        Color color = true == message.alert ? Color.white : Color.yellow;
        messageText.color = color;
        StartCoroutine(TextCor());
    }

    public IEnumerator TextCor()
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(bg.rectTransform);
        CorRunning = true;
        yield return new WaitForEndOfFrame();
        messageText.gameObject.SetActive(true);
        bg.gameObject.SetActive(true);
        StartCoroutine(ModuleManager.FadeModule_Text(messageText,0,1,0.3f));
        StartCoroutine(ModuleManager.FadeModule_Image(bg, 0, 1, 0.3f));
        yield return new WaitForSeconds(1.0f);
        StartCoroutine(ModuleManager.FadeModule_Text(messageText, 1, 0, 0.5f));
        StartCoroutine(ModuleManager.FadeModule_Image(bg, 1, 0, 0.5f));
        yield return new WaitForSeconds(0.5f);
        messageText.gameObject.SetActive(false);
        bg.gameObject.SetActive(false);
        CorRunning = false;

    }
}
