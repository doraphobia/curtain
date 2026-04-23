using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class StoryTextSequenceUI : MonoBehaviour
{
    [Header("Sequence")]
    public GameObject[] textObjects;
    [Min(0.1f)]
    public float displayDuration = 5f;
    public bool playOnEnable = true;

    [Header("Finish")]
    public GameObject buttonToActivate;

    private Coroutine sequenceCoroutine;

    void OnEnable()
    {
        if (playOnEnable)
            BeginSequence();
    }

    void OnDisable()
    {
        if (sequenceCoroutine != null)
        {
            StopCoroutine(sequenceCoroutine);
            sequenceCoroutine = null;
        }

        HideAllTextObjects();
    }

    public void BeginSequence()
    {
        if (sequenceCoroutine != null)
            StopCoroutine(sequenceCoroutine);

        if (buttonToActivate != null)
            buttonToActivate.SetActive(false);

        sequenceCoroutine = StartCoroutine(PlaySequence());
    }

    private IEnumerator PlaySequence()
    {
        HideAllTextObjects();

        if (textObjects == null || textObjects.Length == 0)
        {
            ShowFinishButton();
            sequenceCoroutine = null;
            yield break;
        }

        for (int i = 0; i < textObjects.Length; i++)
        {
            HideAllTextObjects();

            GameObject currentText = textObjects[i];
            if (currentText != null)
                currentText.SetActive(true);

            yield return new WaitForSeconds(displayDuration);
        }

        HideAllTextObjects();
        ShowFinishButton();
        sequenceCoroutine = null;
    }

    private void HideAllTextObjects()
    {
        if (textObjects == null)
            return;

        for (int i = 0; i < textObjects.Length; i++)
        {
            if (textObjects[i] != null)
                textObjects[i].SetActive(false);
        }
    }

    private void ShowFinishButton()
    {
        if (buttonToActivate != null)
            buttonToActivate.SetActive(true);
    }
}
