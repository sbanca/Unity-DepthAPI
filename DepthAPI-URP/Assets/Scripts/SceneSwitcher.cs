using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SceneSwitcher : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("If set, load by name; otherwise use Build Index.")]
    [SerializeField] private string m_sceneName;
    [SerializeField] private int m_sceneBuildIndex = -1;

    [Header("Switch Between")]
    [SerializeField] private string m_sceneA;
    [SerializeField] private string m_sceneB;

    [Header("Options")]
    [SerializeField] private bool m_loadAdditive;

    public void LoadTargetScene()
    {
        if (!string.IsNullOrWhiteSpace(m_sceneName))
        {
            SceneManager.LoadScene(m_sceneName, m_loadAdditive ? LoadSceneMode.Additive : LoadSceneMode.Single);
            return;
        }

        if (m_sceneBuildIndex >= 0)
        {
            SceneManager.LoadScene(m_sceneBuildIndex, m_loadAdditive ? LoadSceneMode.Additive : LoadSceneMode.Single);
            return;
        }

        Debug.LogWarning($"{nameof(SceneSwitcher)}: No target scene set.");
    }

    public void SwitchBetweenConfiguredScenes()
    {
        SwitchBetween(m_sceneA, m_sceneB);
    }

    public void SwitchBetween(string sceneA, string sceneB)
    {
        if (string.IsNullOrWhiteSpace(sceneA) || string.IsNullOrWhiteSpace(sceneB))
        {
            Debug.LogWarning($"{nameof(SceneSwitcher)}: Both scene names must be set.");
            return;
        }

        var active = SceneManager.GetActiveScene();
        var target = string.Equals(active.name, sceneA) ? sceneB : sceneA;
        SceneManager.LoadScene(target, LoadSceneMode.Single);
    }

    public void LoadByName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning($"{nameof(SceneSwitcher)}: Scene name is empty.");
            return;
        }

        SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    public void LoadByIndex(int buildIndex)
    {
        if (buildIndex < 0)
        {
            Debug.LogWarning($"{nameof(SceneSwitcher)}: Build index must be >= 0.");
            return;
        }

        SceneManager.LoadScene(buildIndex, LoadSceneMode.Single);
    }

    public void LoadAdditiveByName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning($"{nameof(SceneSwitcher)}: Scene name is empty.");
            return;
        }

        SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
    }

    public void LoadAdditiveByIndex(int buildIndex)
    {
        if (buildIndex < 0)
        {
            Debug.LogWarning($"{nameof(SceneSwitcher)}: Build index must be >= 0.");
            return;
        }

        SceneManager.LoadScene(buildIndex, LoadSceneMode.Additive);
    }

    public void UnloadByName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning($"{nameof(SceneSwitcher)}: Scene name is empty.");
            return;
        }

        _ = SceneManager.UnloadSceneAsync(sceneName);
    }

    public void UnloadByIndex(int buildIndex)
    {
        if (buildIndex < 0)
        {
            Debug.LogWarning($"{nameof(SceneSwitcher)}: Build index must be >= 0.");
            return;
        }

        _ = SceneManager.UnloadSceneAsync(buildIndex);
    }

    public void ReloadCurrent()
    {
        var current = SceneManager.GetActiveScene();
        if (current.IsValid())
        {
            SceneManager.LoadScene(current.buildIndex, LoadSceneMode.Single);
        }
    }
}
