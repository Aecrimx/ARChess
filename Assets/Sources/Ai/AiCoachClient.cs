using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

public sealed class AiCoachClient : MonoBehaviour
{
    public const string BaseUrlPrefKey = "AiCoachBaseUrl";
    public const string FunctionKeyPrefKey = "AiCoachFunctionKey";
    public const string DefaultBaseUrl = "https://archess-ai-func-12345.redforest-3cb705dd.francecentral.azurecontainerapps.io";
    public const string DefaultFunctionKey = "";

    private static AiCoachClient _instance;

    public static AiCoachClient EnsureInScene()
    {
        if (_instance != null)
        {
            return _instance;
        }

        AiCoachClient existing = FindAnyObjectByType<AiCoachClient>();
        if (existing != null)
        {
            _instance = existing;
            return existing;
        }

        GameObject host = GameStateManager.Instance != null
            ? GameStateManager.Instance.gameObject
            : new GameObject("AiCoachClient");

        _instance = host.AddComponent<AiCoachClient>();
        return _instance;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this);
            return;
        }

        _instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    public IEnumerator AnalyzeMove(
        AiAnalyzeMoveRequest payload,
        Action<string> onSuccess,
        Action<string> onError)
    {
        yield return PostJson<AiAnalyzeMoveRequest, AiAnalyzeMoveResponse>(
            "analyze-move",
            payload,
            response => onSuccess?.Invoke(response?.Feedback ?? string.Empty),
            onError,
            30);
    }

    public IEnumerator ReviewGame(
        AiGameReviewRequest payload,
        Action<string> onSuccess,
        Action<string> onError)
    {
        yield return PostJson<AiGameReviewRequest, AiGameReviewResponse>(
            "review-game",
            payload,
            response => onSuccess?.Invoke(response?.Review ?? string.Empty),
            onError,
            90);
    }

    private IEnumerator PostJson<TRequest, TResponse>(
        string route,
        TRequest payload,
        Action<TResponse> onSuccess,
        Action<string> onError,
        int timeoutSeconds)
    {
        string baseUrl = PlayerPrefs.GetString(BaseUrlPrefKey, DefaultBaseUrl).Trim();
        if (string.IsNullOrEmpty(baseUrl))
        {
            onError?.Invoke("AI coach endpoint is not configured.");
            yield break;
        }

        string url = $"{baseUrl.TrimEnd('/')}/{route.TrimStart('/')}";
        string json = JsonConvert.SerializeObject(payload);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");

            string functionKey = PlayerPrefs.GetString(FunctionKeyPrefKey, DefaultFunctionKey).Trim();
            if (!string.IsNullOrEmpty(functionKey))
            {
                request.SetRequestHeader("x-functions-key", functionKey);
            }

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(BuildErrorMessage(request));
                yield break;
            }

            try
            {
                TResponse response = JsonConvert.DeserializeObject<TResponse>(request.downloadHandler.text);
                onSuccess?.Invoke(response);
            }
            catch (Exception ex)
            {
                onError?.Invoke($"AI coach returned an unreadable response: {ex.Message}");
            }
        }
    }

    private static string BuildErrorMessage(UnityWebRequest request)
    {
        if (request.responseCode > 0)
        {
            return $"AI coach request failed ({request.responseCode}): {request.downloadHandler.text}";
        }

        return $"AI coach request failed: {request.error}";
    }
}

[Serializable]
public sealed class AiAnalyzeMoveRequest
{
    [JsonProperty("fen_before")] public string FenBefore;
    [JsonProperty("fen_after")] public string FenAfter;
    [JsonProperty("move_played")] public string MovePlayed;
    [JsonProperty("player_color")] public string PlayerColor;
    [JsonProperty("move_number")] public int MoveNumber;
}

[Serializable]
public sealed class AiAnalyzeMoveResponse
{
    [JsonProperty("feedback")] public string Feedback;
}

[Serializable]
public sealed class AiGameReviewRequest
{
    [JsonProperty("player_color")] public string PlayerColor;
    [JsonProperty("moves_uci")] public List<string> MovesUci = new List<string>();
    [JsonProperty("final_fen")] public string FinalFen;
    [JsonProperty("pgn")] public string Pgn;
}

[Serializable]
public sealed class AiGameReviewResponse
{
    [JsonProperty("review")] public string Review;
}
