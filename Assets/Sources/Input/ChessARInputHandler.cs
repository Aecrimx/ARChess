using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ChessARInputHandler : ChessBoardInputBase
{
    private static readonly List<ARRaycastHit> PlaneHits = new List<ARRaycastHit>();
    private static readonly Vector2[] PlacementViewportSamples =
    {
        new Vector2(0.5f, 0.35f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.35f, 0.35f),
        new Vector2(0.65f, 0.35f),
        new Vector2(0.5f, 0.2f)
    };

    private const TrackableType PlacementTrackableTypes =
        TrackableType.PlaneWithinPolygon;

    [Header("Dependencies")]
    public Camera arCamera;
    public ARSession arSession;
    public ARRaycastManager raycastManager;
    public ARAnchorManager anchorManager;
    public ARPlaneManager planeManager;
    public ChessARRenderer arRenderer;
    public PawnPromotionPicker promotionPicker;

    [Header("Highlight Colors")]
    public Color selectedColor = new Color(0.20f, 0.85f, 0.20f, 0.60f);
    public Color legalMoveColor = new Color(0.20f, 0.60f, 1.00f, 0.50f);
    public Color lastMoveColor = new Color(1.00f, 0.85f, 0.00f, 0.40f);
    public Color checkColor = new Color(1.00f, 0.10f, 0.10f, 0.55f);

    public bool HasPlacedBoard => arRenderer != null && arRenderer.IsPlaced;

    private bool _isActive;
    private bool _localPlayerIsWhite = true;
    private ChessNetworkProxy _localProxy;
    private ChessMoveInteractionController _interactionController;
    private int _activePointerId = -1;
    private bool _planeVisualsVisible;
    private bool _hasPlacementPose;
    private Pose _placementPose;
    private GameObject _placementIndicator;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnDestroy()
    {
        if (planeManager != null)
        {
            planeManager.trackablesChanged.RemoveListener(HandlePlanesChanged);
        }

        _interactionController?.Dispose();
    }

    private void Update()
    {
        if (!_isActive)
        {
            return;
        }

        if (!TryGetPointerState(out int pointerId, out Vector2 screenPosition, out PointerState pointerState))
        {
            _activePointerId = -1;
            if (!HasPlacedBoard)
            {
                UpdatePlacementPose(null);
            }

            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId))
        {
            if (pointerState == PointerState.Up && pointerId == _activePointerId)
            {
                _activePointerId = -1;
            }

            return;
        }

        if (!HasPlacedBoard)
        {
            UpdatePlacementPose(screenPosition);
            if (pointerState == PointerState.Down)
            {
                TryPlaceBoard(screenPosition);
            }

            return;
        }

        if (pointerState == PointerState.Down)
        {
            if (_activePointerId != -1)
            {
                return;
            }

            if (TryGetSquare(screenPosition, out Vector2Int square))
            {
                _activePointerId = pointerId;
                _interactionController?.OnSquarePointerDown(square);
            }

            return;
        }

        if (pointerState == PointerState.Held && pointerId == _activePointerId)
        {
            if (TryGetSquare(screenPosition, out Vector2Int square))
            {
                _interactionController?.OnSquarePointerEnter(square);
            }

            return;
        }

        if (pointerState == PointerState.Up && pointerId == _activePointerId)
        {
            if (TryGetSquare(screenPosition, out Vector2Int square))
            {
                _interactionController?.OnSquarePointerUp(square);
            }

            _activePointerId = -1;
        }
    }

    public override void Activate()
    {
        EnsureInitialized();
        _isActive = true;
        arRenderer?.Activate();
        _interactionController?.Activate();
        SetPlaneVisibility(!HasPlacedBoard);
        if (!HasPlacedBoard)
        {
            UpdatePlacementPose(null);
        }

        UpdatePlacementIndicator();
    }

    public override void Deactivate()
    {
        _isActive = false;
        _interactionController?.Deactivate();
        arRenderer?.Deactivate();
        _activePointerId = -1;
        _hasPlacementPose = false;
        UpdatePlacementIndicator();
    }

    public override void SetLocalPlayerIsWhite(bool isWhite)
    {
        _localPlayerIsWhite = isWhite;
        arRenderer?.SetPerspective(isWhite);
        _interactionController?.SetContext(_localPlayerIsWhite, _localProxy);
    }

    public override void SetLocalProxy(ChessNetworkProxy proxy)
    {
        _localProxy = proxy;
        _interactionController?.SetContext(_localPlayerIsWhite, _localProxy);
    }

    public void RepositionBoard()
    {
        ClearBoardPlacement();
        SetPlaneVisibility(true);
        RestartPlaneDetection();
        UpdatePlacementPose(null);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        Debug.Log("[ChessARInputHandler] Reposition requested; AR plane detection restarted.");
#endif
    }

    public void ClearBoardPlacement()
    {
        arRenderer?.ClearPlacement();
        _activePointerId = -1;
        _hasPlacementPose = false;
        _interactionController?.Deactivate();
        UpdatePlacementIndicator();
    }

    public void RotateBoard(float degrees)
    {
        arRenderer?.RotateBoard(degrees);
    }

    public void AdjustBoardScale(float delta)
    {
        arRenderer?.AdjustScale(delta);
        UpdatePlacementIndicator();
    }

    private void EnsureInitialized()
    {
        if (_interactionController != null)
        {
            return;
        }

        arCamera ??= GetComponentInChildren<Camera>(true);
        arSession ??= GetComponentInChildren<ARSession>(true);
        raycastManager ??= GetComponentInChildren<ARRaycastManager>(true);
        anchorManager ??= GetComponentInChildren<ARAnchorManager>(true);
        planeManager ??= GetComponentInChildren<ARPlaneManager>(true);
        arRenderer ??= GetComponentInChildren<ChessARRenderer>(true);
        promotionPicker ??= FindAnyObjectByType<PawnPromotionPicker>();

        if (arRenderer == null)
        {
            Debug.LogError("[ChessARInputHandler] ChessARRenderer not found.");
            return;
        }

        _interactionController = new ChessMoveInteractionController(
            arRenderer,
            promotionPicker,
            new ChessMoveInteractionController.HighlightPalette(
                selectedColor,
                legalMoveColor,
                lastMoveColor,
                checkColor));
        _interactionController.SetContext(_localPlayerIsWhite, _localProxy);

        if (planeManager != null)
        {
            planeManager.trackablesChanged.RemoveListener(HandlePlanesChanged);
            planeManager.trackablesChanged.AddListener(HandlePlanesChanged);
        }

        EnsurePlacementIndicator();
    }

    private void TryPlaceBoard(Vector2 screenPosition)
    {
        if (raycastManager == null || arRenderer == null)
        {
            return;
        }

        if (!TryGetPlacementHit(screenPosition, out ARRaycastHit placementHit))
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"[ChessARInputHandler] Placement tap missed. screenPosition={screenPosition}");
#endif
            return;
        }

        Pose pose = placementHit.pose;
        ARAnchor anchor = TryCreatePlacementAnchor(placementHit);
        if (arRenderer.PlaceBoard(pose, arCamera != null ? arCamera.transform : null, anchor))
        {
            _hasPlacementPose = false;
            SetPlaneVisibility(false);
            _interactionController?.Activate();
            UpdatePlacementIndicator();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"[ChessARInputHandler] Board placed at {pose.position}; anchored={anchor != null}");
#endif
        }
        else if (anchor != null)
        {
            Destroy(anchor.gameObject);
        }
    }

    private bool TryGetSquare(Vector2 screenPosition, out Vector2Int square)
    {
        square = default;
        if (arCamera == null)
        {
            return false;
        }

        Ray ray = arCamera.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out RaycastHit hitInfo, 20f))
        {
            return false;
        }

        ARBoardSquare boardSquare = hitInfo.collider.GetComponent<ARBoardSquare>();
        if (boardSquare == null)
        {
            return false;
        }

        square = boardSquare.Square;
        return true;
    }

    private void SetPlaneVisibility(bool visible)
    {
        _planeVisualsVisible = visible;

        if (planeManager == null)
        {
            UpdatePlacementIndicator();
            return;
        }

        if (visible)
        {
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
        }

        planeManager.enabled = visible;
        foreach (ARPlane plane in planeManager.trackables)
        {
            SetPlaneVisualState(plane, visible);
        }

        UpdatePlacementIndicator();
    }

    private void RestartPlaneDetection()
    {
        if (planeManager != null)
        {
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            planeManager.enabled = true;
        }

        if (arSession != null && arSession.enabled)
        {
            arSession.Reset();
        }
    }

    private void HandlePlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        foreach (ARPlane plane in args.added)
        {
            SetPlaneVisualState(plane, _planeVisualsVisible);
        }

        foreach (ARPlane plane in args.updated)
        {
            SetPlaneVisualState(plane, _planeVisualsVisible);
        }

        foreach (KeyValuePair<TrackableId, ARPlane> removedPlane in args.removed)
        {
            ARPlane plane = removedPlane.Value;
            if (plane != null && plane.gameObject != null)
            {
                plane.gameObject.SetActive(false);
            }
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        int totalPlaneCount = planeManager != null ? planeManager.trackables.count : -1;
        Debug.Log($"[ChessARInputHandler] planes changed: added={args.added.Count} updated={args.updated.Count} removed={args.removed.Count} total={totalPlaneCount}");
#endif
    }

    private void UpdatePlacementPose(Vector2? screenPosition)
    {
        _hasPlacementPose = TryResolvePlacementPose(screenPosition, out _placementPose);
        UpdatePlacementIndicator();
    }

    private bool TryResolvePlacementPose(Vector2? preferredScreenPosition, out Pose pose)
    {
        pose = default;

        if (preferredScreenPosition.HasValue && TryGetPlacementPose(preferredScreenPosition.Value, out pose))
        {
            return true;
        }

        foreach (Vector2 viewportSample in PlacementViewportSamples)
        {
            Vector2 sampleScreenPosition = new Vector2(
                Screen.width * viewportSample.x,
                Screen.height * viewportSample.y);
            if (TryGetPlacementPose(sampleScreenPosition, out pose))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetPlacementPose(Vector2 screenPosition, out Pose pose)
    {
        pose = default;

        if (!TryGetPlacementHit(screenPosition, out ARRaycastHit hit))
        {
            return false;
        }

        pose = hit.pose;
        return true;
    }

    private bool TryGetPlacementHit(Vector2 screenPosition, out ARRaycastHit hit)
    {
        hit = default;

        if (raycastManager == null)
        {
            return false;
        }

        if (!raycastManager.Raycast(screenPosition, PlaneHits, PlacementTrackableTypes) || PlaneHits.Count == 0)
        {
            return false;
        }

        hit = PlaneHits[0];
        return true;
    }

    private ARAnchor TryCreatePlacementAnchor(ARRaycastHit hit)
    {
        if (anchorManager == null || !anchorManager.enabled || anchorManager.subsystem == null)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning("[ChessARInputHandler] ARAnchorManager is unavailable; placing without an AR anchor.");
#endif
            return null;
        }

        ARPlane plane = planeManager != null ? planeManager.GetPlane(hit.trackableId) : null;
        if (plane == null)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning($"[ChessARInputHandler] Raycast hit did not resolve to an ARPlane. trackableId={hit.trackableId}");
#endif
            return null;
        }

        if (anchorManager.descriptor == null || !anchorManager.descriptor.supportsTrackableAttachments)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning("[ChessARInputHandler] This anchor subsystem does not support anchors attached to planes; placing without an AR anchor.");
#endif
            return null;
        }

        try
        {
            return anchorManager.AttachAnchor(plane, hit.pose);
        }
        catch (Exception ex)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.LogWarning($"[ChessARInputHandler] Failed to attach AR anchor to plane: {ex.Message}");
#endif
            return null;
        }
    }

    private void EnsurePlacementIndicator()
    {
        if (_placementIndicator != null)
        {
            return;
        }

        _placementIndicator = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _placementIndicator.name = "ARPlacementIndicator";
        _placementIndicator.transform.SetParent(transform, false);
        UpdatePlacementIndicatorScale();

        Collider indicatorCollider = _placementIndicator.GetComponent<Collider>();
        if (indicatorCollider != null)
        {
            Destroy(indicatorCollider);
        }

        Renderer renderer = _placementIndicator.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material material = CreateIndicatorMaterial(new Color(0.15f, 0.85f, 0.95f, 0.55f));
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        _placementIndicator.SetActive(false);
    }

    private void UpdatePlacementIndicator()
    {
        if (_placementIndicator == null)
        {
            return;
        }

        bool shouldShow = _isActive && !HasPlacedBoard && _planeVisualsVisible && _hasPlacementPose;
        _placementIndicator.SetActive(shouldShow);
        if (!shouldShow)
        {
            return;
        }

        UpdatePlacementIndicatorScale();
        _placementIndicator.transform.SetPositionAndRotation(
            _placementPose.position + Vector3.up * 0.0025f,
            _placementPose.rotation * Quaternion.Euler(90f, 0f, 0f));
    }

    private void UpdatePlacementIndicatorScale()
    {
        if (_placementIndicator == null)
        {
            return;
        }

        Vector2 footprint = arRenderer != null ? arRenderer.BoardFootprint : new Vector2(0.5f, 0.5f);
        _placementIndicator.transform.localScale = new Vector3(footprint.x, footprint.y, 1f);
    }

    private static void SetPlaneVisualState(ARPlane plane, bool visible)
    {
        if (plane == null || plane.gameObject == null)
        {
            return;
        }

        if (!plane.gameObject.activeSelf)
        {
            plane.gameObject.SetActive(true);
        }

        MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.enabled = visible;
        }

        MeshCollider meshCollider = plane.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.enabled = visible;
        }

        ARPlaneMeshVisualizer meshVisualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
        if (meshVisualizer != null)
        {
            meshVisualizer.enabled = visible;
        }
    }

    private static Material CreateIndicatorMaterial(Color color)
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

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        ConfigureTransparentMaterial(material);
        return material;
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

    private static bool TryGetPointerState(out int pointerId, out Vector2 position, out PointerState state)
    {
#if ENABLE_INPUT_SYSTEM
        if (TryGetInputSystemPointerState(out pointerId, out position, out state))
        {
            return true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (TryGetLegacyPointerState(out pointerId, out position, out state))
        {
            return true;
        }
#endif

        pointerId = -1;
        position = Vector2.zero;
        state = PointerState.None;
        return false;
    }

#if ENABLE_INPUT_SYSTEM
    private static bool TryGetInputSystemPointerState(out int pointerId, out Vector2 position, out PointerState state)
    {
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            pointerId = touchscreen.primaryTouch.touchId.ReadValue();
            position = touchscreen.primaryTouch.position.ReadValue();

            if (touchscreen.primaryTouch.press.wasPressedThisFrame)
            {
                state = PointerState.Down;
                return true;
            }

            if (touchscreen.primaryTouch.press.wasReleasedThisFrame)
            {
                state = PointerState.Up;
                return true;
            }

            if (touchscreen.primaryTouch.press.isPressed)
            {
                state = PointerState.Held;
                return true;
            }
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            pointerId = mouse.deviceId;
            position = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
            {
                state = PointerState.Down;
                return true;
            }

            if (mouse.leftButton.isPressed)
            {
                state = PointerState.Held;
                return true;
            }

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                state = PointerState.Up;
                return true;
            }
        }

        pointerId = -1;
        position = Vector2.zero;
        state = PointerState.None;
        return false;
    }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
    private static bool TryGetLegacyPointerState(out int pointerId, out Vector2 position, out PointerState state)
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            pointerId = touch.fingerId;
            position = touch.position;
            state = touch.phase switch
            {
                UnityEngine.TouchPhase.Began => PointerState.Down,
                UnityEngine.TouchPhase.Moved => PointerState.Held,
                UnityEngine.TouchPhase.Stationary => PointerState.Held,
                UnityEngine.TouchPhase.Ended => PointerState.Up,
                UnityEngine.TouchPhase.Canceled => PointerState.Up,
                _ => PointerState.None
            };
            return state != PointerState.None;
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        pointerId = -1;
        position = Input.mousePosition;
        if (Input.GetMouseButtonDown(0))
        {
            state = PointerState.Down;
            return true;
        }

        if (Input.GetMouseButton(0))
        {
            state = PointerState.Held;
            return true;
        }

        if (Input.GetMouseButtonUp(0))
        {
            state = PointerState.Up;
            return true;
        }
#endif

        pointerId = -1;
        position = Vector2.zero;
        state = PointerState.None;
        return false;
    }
#endif

    private enum PointerState
    {
        None,
        Down,
        Held,
        Up
    }
}
