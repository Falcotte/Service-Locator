using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AngryKoala.Services
{
    public sealed class ServiceInspectorEditorWindow : EditorWindow
    {
        private readonly List<ServiceData> _services = new();
        private IReadOnlyDictionary<Type, IService> _servicesByType;

        private bool _autoRefreshEnabled = true;
        private double _nextAutoRefreshTime;

        private Vector2 _scroll;

        private const float _horizontalPadding = 6f;
        private const float _buttonHorizontalInset = 3f;
        private const float _buttonVerticalInset = 3f;
        private const float _buttonMinHeight = 20f;

        private const string _autoRefreshPref = "ServiceInspector.AutoRefresh";

        private const double _autoRefreshInterval = 0.05;

        private const float _headerHeight = 26f;
        private const float _headerLabelLeftPad = 10f;
        private const float _headerSpacing = 2f;

        private const float _rowVerticalSpacing = 2f;
        private const float _rowHeight = 34f;
        private const float _rowInnerPadding = 5f;
        private const float _cellPadX = 6f;

        private static GUIStyle _headerStyle;
        private static GUIStyle _backgroundStyle;
        private static GUIStyle _pingButtonStyle;

        private static readonly Color _headerDarkColor = new(0.18f, 0.18f, 0.18f, 1f);
        private static readonly Color _headerLightColor = new(0.26f, 0.26f, 0.26f, 1f);
        private static readonly Color _cellDarkColor = new(0.18f, 0.18f, 0.18f, 1f);
        private static readonly Color _cellLightColor = new(0.26f, 0.26f, 0.26f, 1f);

        private void OnEnable()
        {
            _autoRefreshEnabled = EditorPrefs.GetBool(_autoRefreshPref, true);
            _nextAutoRefreshTime = EditorApplication.timeSinceStartup + _autoRefreshInterval;

            ServiceLocator.OnServiceRegistered += HandleServiceRegistered;
            ServiceLocator.OnServiceDeregistered += HandleServiceDeregistered;

            RefreshReflection();
            SetServices();

            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            ServiceLocator.OnServiceRegistered -= HandleServiceRegistered;
            ServiceLocator.OnServiceDeregistered -= HandleServiceDeregistered;

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            _services.Clear();
        }

        [MenuItem("Angry Koala/Services/Service Inspector")]
        private static void OpenWindow()
        {
            ServiceInspectorEditorWindow window = GetWindow<ServiceInspectorEditorWindow>("Service Inspector");

            window.minSize = new Vector2(900f, 400f);
            window.Show();
        }

        private void RefreshReflection()
        {
            try
            {
                Type serviceLocatorType = typeof(ServiceLocator);

                FieldInfo servicesByTypeField =
                    serviceLocatorType.GetField("_servicesByType", BindingFlags.NonPublic | BindingFlags.Static);

                _servicesByType = servicesByTypeField != null
                    ? servicesByTypeField.GetValue(null) as IReadOnlyDictionary<Type, IService>
                    : null;
            }
            catch
            {
                _servicesByType = null;
            }
        }

        private void OnEditorUpdate()
        {
            if (!_autoRefreshEnabled || !Application.isPlaying)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now >= _nextAutoRefreshTime)
            {
                _nextAutoRefreshTime = now + _autoRefreshInterval;
                SetServices();
                Repaint();
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SetServices();
                Repaint();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                _services.Clear();
                Repaint();
            }
        }

        private void SetServices()
        {
            _services.RemoveAll(serviceData => serviceData.ServiceInstance == null);

            if (!Application.isPlaying)
            {
                return;
            }

            if (_servicesByType == null)
            {
                return;
            }

            foreach (KeyValuePair<Type, IService> pair in _servicesByType)
            {
                IService service = pair.Value;
                if (service == null)
                {
                    continue;
                }

                int existing = _services.FindIndex(e => ReferenceEquals(e.ServiceInstance, service));
                if (existing >= 0)
                {
                    _services[existing] = GetServiceData(service, pair.Key);
                }
                else
                {
                    _services.Add(GetServiceData(service, pair.Key));
                }
            }

            for (int i = _services.Count - 1; i >= 0; i--)
            {
                if (!IsServiceRegistered(_services[i].ServiceInstance, _servicesByType))
                {
                    _services.RemoveAt(i);
                }
            }
        }

        private ServiceData GetServiceData(IService service, Type explicitType = null)
        {
            Type type = explicitType ?? InferServiceInterfaceType(service);
            Object unityObject = ExtractUnityObject(service);

            string implementationName = service.GetType().FullName;

            return new ServiceData
            {
                ServiceType = type,
                ServiceInstance = service,
                ImplementationName = implementationName,
                UnityObject = unityObject
            };
        }

        private static bool IsServiceRegistered(IService service, IReadOnlyDictionary<Type, IService> services)
        {
            foreach (KeyValuePair<Type, IService> keyValuePair in services)
            {
                if (ReferenceEquals(keyValuePair.Value, service))
                {
                    return true;
                }
            }

            return false;
        }

        private void HandleServiceRegistered(IService service)
        {
            if (service == null)
            {
                return;
            }

            ServiceData data = GetServiceData(service);

            int index = _services.FindIndex(e => ReferenceEquals(e.ServiceInstance, service));
            if (index >= 0)
            {
                _services[index] = data;
            }
            else
            {
                _services.Add(data);
            }

            Repaint();
        }

        private void HandleServiceDeregistered(IService service)
        {
            if (service == null)
            {
                return;
            }

            int index = _services.FindIndex(e => ReferenceEquals(e.ServiceInstance, service));
            if (index >= 0)
            {
                _services.RemoveAt(index);
            }

            Repaint();
        }

        private void OnGUI()
        {
            SetGUIStyles();
            DrawToolbar();

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to see registered services.", MessageType.Info);
                return;
            }

            if (_services.Count == 0)
            {
                EditorGUILayout.HelpBox("No services are currently registered.", MessageType.Warning);
                return;
            }

            GUILayout.Space(_headerSpacing);

            Rect headerOuter =
                GUILayoutUtility.GetRect(0, _headerHeight + _rowInnerPadding * 2f, GUILayout.ExpandWidth(true));
            headerOuter.x += _horizontalPadding;
            headerOuter.width -= _horizontalPadding * 2f;
            GUI.Box(headerOuter, GUIContent.none, EditorStyles.helpBox);

            Rect headerInner = new Rect(
                headerOuter.x + _rowInnerPadding,
                headerOuter.y + _rowInnerPadding,
                headerOuter.width - _rowInnerPadding * 2f,
                _headerHeight
            );

            float toolbarHeight = EditorStyles.toolbar.fixedHeight > 0 ? EditorStyles.toolbar.fixedHeight : 18f;
            float headerTotalHeight = _headerHeight + _rowInnerPadding * 2f;
            float blocksGap = 2f;

            float viewportHeight = position.height - toolbarHeight - headerTotalHeight - blocksGap;

            bool scrollbarVisible =
                ScrollbarVisible(_services.Count, _rowHeight, _rowVerticalSpacing,
                    viewportHeight);
            if (scrollbarVisible)
            {
                float scrollbarWidth = GUI.skin.verticalScrollbar.fixedWidth
                                       + GUI.skin.verticalScrollbar.margin.left
                                       + GUI.skin.verticalScrollbar.margin.right;
                headerInner.width -= scrollbarWidth;
            }

            float baseColumnWidth = Mathf.Floor(headerInner.width / 3f);
            float actionsWidth = headerInner.width - baseColumnWidth * 2f;

            DrawHeaders(headerInner, baseColumnWidth, actionsWidth);
            GUILayout.Space(_rowVerticalSpacing);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, false, false, GUILayout.ExpandWidth(true));
            try
            {
                for (int i = 0; i < _services.Count; i++)
                {
                    DrawRow(_services[i], baseColumnWidth, actionsWidth);
                }
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                {
                    SetServices();
                    Repaint();
                }

                GUILayout.FlexibleSpace();

                bool autoRefreshEnabled = GUILayout.Toggle(
                    _autoRefreshEnabled,
                    "Auto-refresh",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(110f));

                if (autoRefreshEnabled != _autoRefreshEnabled)
                {
                    _autoRefreshEnabled = autoRefreshEnabled;

                    EditorPrefs.SetBool(_autoRefreshPref, _autoRefreshEnabled);
                    _nextAutoRefreshTime = EditorApplication.timeSinceStartup + _autoRefreshInterval;
                }
            }
        }

        private void DrawHeaders(Rect rect, float baseColumnWidth, float actionsWidth)
        {
            float x = rect.x;

            DrawHeaderCell("Service Type", new Rect(x, rect.y, baseColumnWidth, rect.height), true);
            x += baseColumnWidth;

            DrawHeaderCell("Implementation", new Rect(x, rect.y, baseColumnWidth, rect.height), false);
            x += baseColumnWidth;

            DrawHeaderCell("Actions", new Rect(x, rect.y, actionsWidth, rect.height), true);
        }

        private void DrawHeaderCell(string label, Rect rect, bool isDark)
        {
            EditorGUI.DrawRect(rect, isDark ? _headerDarkColor : _headerLightColor);

            Rect labelRect = new Rect(rect.x + _headerLabelLeftPad, rect.y, rect.width - _headerLabelLeftPad,
                rect.height);
            GUI.Label(labelRect, label, _headerStyle);
        }

        private void DrawRow(ServiceData data, float columnWidth, float actionsWidth)
        {
            Rect reserved =
                GUILayoutUtility.GetRect(0, _rowHeight + _rowVerticalSpacing * 2f, GUILayout.ExpandWidth(true));

            reserved.x += _horizontalPadding;
            reserved.width -= _horizontalPadding * 2f;

            Rect outer = new Rect(reserved.x, reserved.y + _rowVerticalSpacing, reserved.width, _rowHeight);
            GUI.Box(outer, GUIContent.none, EditorStyles.helpBox);

            Rect inner = new Rect(
                outer.x + _rowInnerPadding,
                outer.y + _rowInnerPadding,
                outer.width - _rowInnerPadding * 2f,
                outer.height - _rowInnerPadding * 2f
            );

            float x = inner.x;
            float height = inner.height;

            EditorGUI.DrawRect(new Rect(x, inner.y, columnWidth, height), _cellDarkColor);
            GUI.Label(new Rect(x + _cellPadX, inner.y, columnWidth - _cellPadX * 2f, height),
                data.ServiceType != null ? data.ServiceType.FullName : "(unknown)", _backgroundStyle);
            x += columnWidth;

            EditorGUI.DrawRect(new Rect(x, inner.y, columnWidth, height), _cellLightColor);
            GUI.Label(new Rect(x + _cellPadX, inner.y, columnWidth - _cellPadX * 2f, height),
                data.ImplementationName ?? "-", _backgroundStyle);
            x += columnWidth;

            Rect actions = new Rect(x, inner.y, actionsWidth, height);
            EditorGUI.DrawRect(actions, _cellDarkColor);

            float insetX = actions.x + _buttonHorizontalInset;
            float insetY = actions.y + _buttonVerticalInset - 1.5f;
            float insetWidth = actions.width - _buttonHorizontalInset * 2f;
            float insetHeight = Mathf.Max(_buttonMinHeight, actions.height - _buttonVerticalInset * 2f);

            Rect pingRect = SnapToPixels(new Rect(insetX, insetY, insetWidth, insetHeight));

            using (new EditorGUI.DisabledScope(data.UnityObject == null))
            {
                if (GUI.Button(pingRect, "Ping", _pingButtonStyle))
                {
                    TryPing(data);
                }
            }
        }

        private void TryPing(ServiceData data)
        {
            if (data.UnityObject == null)
            {
                return;
            }

            EditorGUIUtility.PingObject(data.UnityObject);
            Selection.activeObject = data.UnityObject;
        }

        #region Utility

        private void SetGUIStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = Mathf.Max(12, EditorStyles.boldLabel.fontSize + 1)
                };
            }

            if (_backgroundStyle == null)
            {
                _backgroundStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (_pingButtonStyle == null)
            {
                _pingButtonStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    alignment = TextAnchor.MiddleCenter,
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(4, 4, 3, 3),
                    fixedHeight = 0f,
                    stretchHeight = true,
                    border = new RectOffset(2, 2, 2, 2)
                };
            }
        }

        private static bool ScrollbarVisible(int rowCount, float rowBoxHeight, float rowVerticalSpacing,
            float viewportHeightAvailable)
        {
            float perRow = rowBoxHeight + rowVerticalSpacing * 2f;
            float contentHeight = rowCount * perRow + 1.5f;

            return contentHeight > viewportHeightAvailable;
        }

        private static Rect SnapToPixels(Rect rect)
        {
            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;

            rect.x = Mathf.Round(rect.x * pixelsPerPoint) / pixelsPerPoint;
            rect.y = Mathf.Round(rect.y * pixelsPerPoint) / pixelsPerPoint;
            rect.width = Mathf.Round(rect.width * pixelsPerPoint) / pixelsPerPoint;
            rect.height = Mathf.Round(rect.height * pixelsPerPoint) / pixelsPerPoint;

            return rect;
        }

        private static Object ExtractUnityObject(IService service)
        {
            MonoBehaviour monoBehaviour = service as MonoBehaviour;
            if (monoBehaviour != null)
            {
                if (monoBehaviour.gameObject != null)
                {
                    return monoBehaviour.gameObject;
                }

                return monoBehaviour;
            }

            Object unityObject = service as Object;
            return unityObject;
        }

        private static Type InferServiceInterfaceType(IService service)
        {
            if (service == null)
            {
                return typeof(IService);
            }

            Type type = service.GetType();
            Type[] interfaces = type.GetInterfaces();

            for (int i = 0; i < interfaces.Length; i++)
            {
                Type candidate = interfaces[i];
                if (candidate == typeof(IService))
                {
                    continue;
                }

                if (typeof(IService).IsAssignableFrom(candidate))
                {
                    return candidate;
                }
            }

            return typeof(IService);
        }

        private struct ServiceData
        {
            public Type ServiceType;

            public IService ServiceInstance;

            public string ImplementationName;

            public Object UnityObject;
        }

        #endregion
    }
}