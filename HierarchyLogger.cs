using UnityEngine;
using System.Text;

/// <summary>
/// Attach this to the Dragon Root. It will print its exact hierarchy tree
/// into the text box below so you can easily read it in the Inspector without digging through the console.
/// </summary>
[ExecuteAlways]
public class HierarchyLogger : MonoBehaviour
{
    [Header("Hierarchy Output (Read Only)")]
    [TextArea(10, 50)]
    public string hierarchyOutput;

    public bool clickToRefresh = false;

    private void Update()
    {
        if (clickToRefresh)
        {
            clickToRefresh = false;
            RefreshHierarchy();
        }
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            RefreshHierarchy();
        }
    }

    [ContextMenu("Refresh Hierarchy")]
    public void RefreshHierarchy()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"--- HIERARCHY LOG FOR: {gameObject.name} ---");

        LogChild(transform, "", sb);

        hierarchyOutput = sb.ToString();
    }

    private void LogChild(Transform t, string prefix, StringBuilder sb)
    {
        sb.AppendLine($"{prefix} {t.name}");
        for (int i = 0; i < t.childCount; i++)
        {
            LogChild(t.GetChild(i), prefix + "  |--", sb);
        }
    }
}
