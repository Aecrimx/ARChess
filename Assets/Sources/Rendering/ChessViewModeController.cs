using System;
using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
#endif

public class ChessViewModeController : MonoBehaviour
{
    private const float AvailabilityTimeoutSeconds = 30f;
    private const float AvailabilityLogIntervalSeconds = 5f;

    public static ChessViewModeController Instance { get; private set; }

    public event Action StateChanged;

    public BoardViewMode CurrentViewMode { get; private set; } = BoardViewMode.TwoD;
    public bool IsARSupported => _availabilityKnown && _arSupported;
    public bool IsCheckingAvailability => _checkingAvailability;
    public bool IsARModeActive => CurrentViewMode == BoardViewMode.AR;
    public bool CanToggleAR => IsARModeActive || (Application.platform == RuntimePlatform.Android && (!_availabilityKnown || _arSupported));
    public string AvailabilityMessage => _availabilityMessage;

    private Chess2DRenderer _twoDRenderer;
    private Chess2DInputHandler _twoDInput;
    private ChessARRenderer _arRenderer;
    private ChessARInputHandler _arInput;
    private Camera _normalCamera;
    private AudioListener _normalAudioListener;

    private GameObject _arRoot;
    private ARSession _arSession;
    private Camera _arCamera;
    private AudioListener _arAudioListener;
    private ARPlaneManager _planeManager;
    private ARRaycastManager _raycastManager;

    private bool _availabilityKnown;
    private bool _arSupported;
    private bool _checkingAvailability;
    private bool _switchingMode;
    private string _availabilityMessage = "AR is only available on supported Android devices.";
    private bool _availabilityTimedOut;
    private bool _localPlayerIsWhite = true;
    private ChessNetworkProxy _localProxy;

    public static ChessViewModeController EnsureInScene()
    {
        if (Instance != null)
        {
            return Instance;
        }

        ChessViewModeController existing = FindAnyObjectByType<ChessViewModeController>();
        if (existing != null)
        {
            return existing;
        }

        GameObject host = GameStateManager.Instance != null
            ? GameStateManager.Instance.gameObject
            : FindAnyObjectByType<GameStateManager>()?.gameObject ?? new GameObject("ChessViewModeController");
        return host.AddComponent<ChessViewModeController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        AutoWire2D();
        BuildARRoot();
        ApplyTwoDState();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ToggleARMode()
    {
        if (CurrentViewMode == BoardViewMode.AR)
        {
            ExitARMode();
        }
        else
        {
            EnterARMode();
        }
    }

    public void EnterARMode()
    {
        if (_switchingMode)
        {
            return;
        }

        StartCoroutine(EnterARRoutine());
    }

    public void ExitARMode()
    {
        if (_switchingMode || CurrentViewMode != BoardViewMode.AR)
        {
            return;
        }

        if (_arInput != null)
        {
            _arInput.Deactivate();
            _arInput.ClearBoardPlacement();
        }

        if (_arSession != null)
        {
            _arSession.enabled = false;
        }

        XRManagerSettings xrManager = XRGeneralSettings.Instance?.Manager;
        if (xrManager != null && xrManager.activeLoader != null)
        {
            xrManager.StopSubsystems();
            xrManager.DeinitializeLoader();
        }

        if (_arRoot != null)
        {
            _arRoot.SetActive(false);
        }

        ApplyTwoDState();
        NotifyStateChanged();
    }

    public void ConfigureMatchContext(bool isWhite, ChessNetworkProxy proxy)
    {
        _localPlayerIsWhite = isWhite;
        _localProxy = proxy;

        AutoWire2D();
        BuildARRoot();

        _twoDRenderer?.SetPerspective(isWhite);
        _twoDInput?.SetLocalPlayerIsWhite(isWhite);
        _twoDInput?.SetLocalProxy(proxy);

        _arRenderer?.SetPerspective(isWhite);
        _arInput?.SetLocalPlayerIsWhite(isWhite);
        _arInput?.SetLocalProxy(proxy);

        RefreshAllViews();
    }

    public void RefreshAllViews()
    {
        _twoDRenderer?.RedrawPieces();
        _arRenderer?.RedrawPieces();
        GetActiveInput()?.Activate();
    }

    public ChessBoardInputBase GetActiveInput()
    {
        return CurrentViewMode == BoardViewMode.AR ? _arInput : _twoDInput;
    }

    public void SetActiveInputEnabled(bool active)
    {
        ChessBoardInputBase activeInput = GetActiveInput();
        if (active)
        {
            activeInput?.Activate();
        }
        else
        {
            activeInput?.Deactivate();
        }
    }

    public ChessARInputHandler GetARInput()
    {
        return _arInput;
    }

    private IEnumerator EnterARRoutine()
    {
        _switchingMode = true;

        if (Application.platform != RuntimePlatform.Android)
        {
            _availabilityKnown = true;
            _arSupported = false;
            _availabilityMessage = "AR is only enabled for Android builds in this version.";
            _switchingMode = false;
            NotifyStateChanged();
            yield break;
        }

        XRManagerSettings xrManager = XRGeneralSettings.Instance?.Manager;
        if (xrManager == null)
        {
            _availabilityMessage = "XR Plug-in Management is not configured for this project.";
            _switchingMode = false;
            NotifyStateChanged();
            yield break;
        }

        if (xrManager.activeLoader == null)
        {
            yield return xrManager.InitializeLoader();
        }

        if (xrManager.activeLoader == null)
        {
            _availabilityMessage = "Failed to initialize the AR loader. Check XR Plug-in Management for Android.";
            _switchingMode = false;
            NotifyStateChanged();
            yield break;
        }

        if (!_availabilityKnown || _availabilityTimedOut)
        {
            yield return CheckAvailabilityWithTimeout();
            if (!_arSupported)
            {
                _switchingMode = false;
                NotifyStateChanged();
                yield break;
            }
        }

        if (ARSession.state == ARSessionState.NeedsInstall)
        {
            _availabilityMessage = "Installing or updating Google Play Services for AR...";
            NotifyStateChanged();
            yield return ARSession.Install();
            UpdateAvailabilityStateFromSession();
            if (!_arSupported || ARSession.state == ARSessionState.NeedsInstall)
            {
                _switchingMode = false;
                NotifyStateChanged();
                yield break;
            }
        }

        xrManager.StartSubsystems();

        if (_arRoot != null)
        {
            _arRoot.SetActive(true);
        }

        if (_arSession != null)
        {
            _arSession.enabled = true;
        }

        _twoDInput?.Deactivate();
        _twoDRenderer?.Deactivate();
        if (_normalCamera != null)
        {
            _normalCamera.enabled = false;
        }

        if (_normalAudioListener != null)
        {
            _normalAudioListener.enabled = false;
        }

        if (_arAudioListener != null)
        {
            _arAudioListener.enabled = true;
        }

        _arRenderer?.SetPerspective(_localPlayerIsWhite);
        _arInput?.SetLocalPlayerIsWhite(_localPlayerIsWhite);
        _arInput?.SetLocalProxy(_localProxy);
        _arInput?.Activate();

        CurrentViewMode = BoardViewMode.AR;
        _switchingMode = false;
        NotifyStateChanged();
    }

    private IEnumerator CheckAvailabilityWithTimeout()
    {
        if (_checkingAvailability)
        {
            while (_checkingAvailability)
            {
                yield return null;
            }

            yield break;
        }

        _checkingAvailability = true;
        _availabilityTimedOut = false;
        NotifyStateChanged();

        XRManagerSettings xrManager = XRGeneralSettings.Instance?.Manager;
        string loaderName = xrManager?.activeLoader != null ? xrManager.activeLoader.name : "<none>";
        string subsystemName = xrManager?.activeLoader?.GetLoadedSubsystem<XRSessionSubsystem>() != null
            ? xrManager.activeLoader.GetLoadedSubsystem<XRSessionSubsystem>().GetType().Name
            : "<none>";
        Debug.Log($"[ChessViewModeController] Starting AR support check. loader={loaderName} sessionSubsystem={subsystemName} state={ARSession.state}");

        IEnumerator checkRoutine = ARSession.CheckAvailability();
        float startedAt = Time.realtimeSinceStartup;
        float nextLogAt = startedAt + AvailabilityLogIntervalSeconds;
        bool completed = false;

        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = checkRoutine.MoveNext();
            }
            catch (Exception ex)
            {
                _availabilityKnown = true;
                _arSupported = false;
                _availabilityMessage = $"AR support check failed: {ex.Message}";
                _checkingAvailability = false;
                NotifyStateChanged();
                yield break;
            }

            if (!hasNext)
            {
                completed = true;
                break;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (Time.realtimeSinceStartup >= nextLogAt)
            {
                xrManager = XRGeneralSettings.Instance?.Manager;
                loaderName = xrManager?.activeLoader != null ? xrManager.activeLoader.name : "<none>";
                subsystemName = xrManager?.activeLoader?.GetLoadedSubsystem<XRSessionSubsystem>() != null
                    ? xrManager.activeLoader.GetLoadedSubsystem<XRSessionSubsystem>().GetType().Name
                    : "<none>";
                Debug.Log($"[ChessViewModeController] Still checking AR support... elapsed={Time.realtimeSinceStartup - startedAt:F1}s loader={loaderName} sessionSubsystem={subsystemName} state={ARSession.state}");
                nextLogAt += AvailabilityLogIntervalSeconds;
            }
#else
            if (Time.realtimeSinceStartup - startedAt >= AvailabilityTimeoutSeconds)
            {
                break;
            }
#endif

            yield return checkRoutine.Current;
        }

        _checkingAvailability = false;

        if (!completed)
        {
            _availabilityKnown = false;
            _arSupported = false;
            _availabilityTimedOut = true;
            _availabilityMessage = "AR support check is taking longer than expected. Make sure the device has internet access and Google Play Services for AR is available, then try Enter AR again.";
            NotifyStateChanged();
            yield break;
        }

        UpdateAvailabilityStateFromSession();
        Debug.Log($"[ChessViewModeController] Finished AR support check. state={ARSession.state} supported={_arSupported}");
        NotifyStateChanged();
    }

    private void UpdateAvailabilityStateFromSession()
    {
        _availabilityTimedOut = false;
        _availabilityKnown = true;

        switch (ARSession.state)
        {
            case ARSessionState.NeedsInstall:
                _arSupported = true;
                _availabilityMessage = "AR support is available, but Google Play Services for AR still needs to be installed or updated.";
                return;
            case ARSessionState.Installing:
                _arSupported = true;
                _availabilityMessage = "Installing or updating Google Play Services for AR...";
                return;
            case ARSessionState.Ready:
            case ARSessionState.SessionInitializing:
            case ARSessionState.SessionTracking:
                _arSupported = true;
                _availabilityMessage = string.Empty;
                return;
            case ARSessionState.Unsupported:
                _arSupported = false;
                _availabilityMessage = "ARCore is not available on this device.";
                return;
            case ARSessionState.None:
                _arSupported = false;
                _availabilityMessage = "AR support could not be determined on this device.";
                return;
            default:
                _arSupported = false;
                _availabilityMessage = "ARCore is not available on this device.";
                return;
        }
    }

    private void ApplyTwoDState()
    {
        AutoWire2D();

        CurrentViewMode = BoardViewMode.TwoD;
        if (_arRoot != null)
        {
            _arRoot.SetActive(false);
        }

        if (_normalCamera != null)
        {
            _normalCamera.enabled = true;
        }

        if (_normalAudioListener != null)
        {
            _normalAudioListener.enabled = true;
        }

        if (_arAudioListener != null)
        {
            _arAudioListener.enabled = false;
        }

        _twoDRenderer?.SetPerspective(_localPlayerIsWhite);
        _twoDInput?.SetLocalPlayerIsWhite(_localPlayerIsWhite);
        _twoDInput?.SetLocalProxy(_localProxy);
        _twoDRenderer?.Activate();
        _twoDInput?.Activate();
    }

    private void AutoWire2D()
    {
        _twoDRenderer ??= FindAnyObjectByType<Chess2DRenderer>();
        _twoDInput ??= FindAnyObjectByType<Chess2DInputHandler>();
        _normalCamera ??= Camera.main;
        if (_normalCamera == null)
        {
            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (camera == _arCamera)
                {
                    continue;
                }

                _normalCamera = camera;
                break;
            }
        }

        _normalAudioListener ??= _normalCamera != null ? _normalCamera.GetComponent<AudioListener>() : null;
    }

    private void BuildARRoot()
    {
        if (_arRoot != null)
        {
            return;
        }

        _arRoot = new GameObject("ARRoot");
        _arRoot.transform.SetParent(transform, false);

        GameObject sessionObject = new GameObject("ARSession");
        sessionObject.transform.SetParent(_arRoot.transform, false);
        _arSession = sessionObject.AddComponent<ARSession>();
        _arSession.enabled = false;

        GameObject originObject = new GameObject("XROrigin");
        originObject.transform.SetParent(_arRoot.transform, false);
        GameObject cameraOffsetObject = new GameObject("CameraOffset");
        cameraOffsetObject.transform.SetParent(originObject.transform, false);

        GameObject cameraObject = new GameObject("ARCamera");
        cameraObject.transform.SetParent(cameraOffsetObject.transform, false);

        XROrigin xrOrigin = originObject.AddComponent<XROrigin>();
        xrOrigin.Origin = originObject;
        xrOrigin.CameraFloorOffsetObject = cameraOffsetObject;
        xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

        _raycastManager = originObject.AddComponent<ARRaycastManager>();
        _planeManager = originObject.AddComponent<ARPlaneManager>();
        _planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        _arCamera = cameraObject.AddComponent<Camera>();
        _arCamera.clearFlags = CameraClearFlags.SolidColor;
        _arCamera.backgroundColor = Color.black;
        _arCamera.nearClipPlane = 0.01f;
        _arCamera.farClipPlane = 20f;
        _arCamera.tag = "MainCamera";
        xrOrigin.Camera = _arCamera;
        _arAudioListener = cameraObject.AddComponent<AudioListener>();
        _arAudioListener.enabled = false;
        cameraObject.AddComponent<ARCameraManager>();
        cameraObject.AddComponent<ARCameraBackground>();
        ConfigureARCameraPoseDriver(cameraObject);

        _planeManager.planePrefab = CreatePlaneTemplate();

        _arRenderer = _arRoot.AddComponent<ChessARRenderer>();
        _arInput = _arRoot.AddComponent<ChessARInputHandler>();
        _arInput.arCamera = _arCamera;
        _arInput.arSession = _arSession;
        _arInput.raycastManager = _raycastManager;
        _arInput.planeManager = _planeManager;
        _arInput.arRenderer = _arRenderer;
        _arInput.promotionPicker = FindAnyObjectByType<PawnPromotionPicker>();

        _arRoot.SetActive(false);
    }

    private GameObject CreatePlaneTemplate()
    {
        var template = new GameObject("ARPlaneTemplate");
        template.transform.SetParent(_arRoot.transform, false);
        template.AddComponent<ARPlane>();
        template.AddComponent<MeshFilter>();
        template.AddComponent<MeshRenderer>();
        template.AddComponent<MeshCollider>();
        template.AddComponent<ARPlaneMeshVisualizer>();

        Renderer renderer = template.GetComponent<MeshRenderer>();
        Material material = CreateTintMaterial(new Color(0.15f, 0.7f, 0.9f, 0.15f));
        if (material != null)
        {
            renderer.sharedMaterial = material;
        }
        else
        {
            renderer.enabled = false;
            Debug.LogWarning("ChessViewModeController could not find a runtime shader for AR plane visualization. Plane detection will still work, but the visual overlay is disabled.");
        }

        return template;
    }

    private static Material CreateTintMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.color = color;
        }

        ConfigureTransparentMaterial(material);
        return material;
    }

    private static void ConfigureARCameraPoseDriver(GameObject cameraObject)
    {
#if ENABLE_INPUT_SYSTEM
        TrackedPoseDriver trackedPoseDriver =
            cameraObject.GetComponent<TrackedPoseDriver>() ?? cameraObject.AddComponent<TrackedPoseDriver>();

        var positionAction = new InputAction("AR Camera Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
        positionAction.AddBinding("<HandheldARInputDevice>/devicePosition");

        var rotationAction = new InputAction("AR Camera Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
        rotationAction.AddBinding("<HandheldARInputDevice>/deviceRotation");

        trackedPoseDriver.positionInput = new InputActionProperty(positionAction);
        trackedPoseDriver.rotationInput = new InputActionProperty(rotationAction);
        trackedPoseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        trackedPoseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
#else
        TryAddComponent(cameraObject, "UnityEngine.SpatialTracking.TrackedPoseDriver, Unity.XR.LegacyInputHelpers");
#endif
    }

    private static void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static void TryAddComponent(GameObject gameObject, string assemblyQualifiedTypeName)
    {
        Type componentType = Type.GetType(assemblyQualifiedTypeName, throwOnError: false);
        if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
        {
            return;
        }

        gameObject.AddComponent(componentType);
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }
}
