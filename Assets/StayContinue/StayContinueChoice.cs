using System.Collections.Generic;
using Gtec.Chain.Common.Templates.Utilities;
using Gtec.UnityInterface;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Turns the two g.tec flash tags of the Stay/Continue scene into a choice: whichever tag the
/// ERP pipeline (or a mouse click, via the tag's own OnMouseDown) selects fires its event.
/// </summary>
public class StayContinueChoice : MonoBehaviour
{
    public ERPTag stay;
    public ERPTag continueTag;
    public UnityEvent onStay = new UnityEvent();
    public UnityEvent onContinue = new UnityEvent();

    void OnEnable()
    {
        if (stay != null) stay.OnTagSelected.AddListener(OnStaySelected);
        if (continueTag != null) continueTag.OnTagSelected.AddListener(OnContinueSelected);
    }

    void OnDisable()
    {
        if (stay != null) stay.OnTagSelected.RemoveListener(OnStaySelected);
        if (continueTag != null) continueTag.OnTagSelected.RemoveListener(OnContinueSelected);
    }

    void OnStaySelected(ERPPipeline pipeline, ClassSelection selection, List<ERPPipeline> pipelines)
    {
        Debug.Log("[StayContinue] STAY selected.");
        onStay.Invoke();
    }

    void OnContinueSelected(ERPPipeline pipeline, ClassSelection selection, List<ERPPipeline> pipelines)
    {
        Debug.Log("[StayContinue] CONTINUE selected.");
        onContinue.Invoke();
    }
}
