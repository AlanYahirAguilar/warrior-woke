using System.IO;
using UnityEngine;

namespace WarriorWoke.EditorTools
{
    /// <summary>
    /// Renders pose contact sheets for the Editor probes (MocapRetargetProbe, VaultCatalogBuilder) and the
    /// Play Mode test: an orthographic side camera and, in the studio setup, a light, a ground plane and
    /// optionally a proxy obstacle box (in the game's scene, its own floor, light and obstacles are
    /// rendered instead). Each cell is one rendered frame; the sheet is saved as a PNG under Logs/ for
    /// visual review.
    /// </summary>
    internal sealed class PoseSheetRenderer : System.IDisposable
    {
        private readonly GameObject _camGo, _lightGo, _ground, _obstacle, _marker;
        private readonly Material _markerMaterial;
        private readonly Camera _cam;
        private readonly RenderTexture _rt;
        private readonly Texture2D _cell;
        private readonly int _cellWidth, _cellHeight;
        private Texture2D _sheet;
        private int _rows;

        /// <param name="studio">True: its own light, ground plane and proxy obstacle (empty scene). False: the scene as it is.</param>
        public PoseSheetRenderer(int cellWidth, int cellHeight, float orthographicSize, bool studio = true)
        {
            _cellWidth = cellWidth;
            _cellHeight = cellHeight;
            _camGo = new GameObject("ProbeCamera");
            _cam = _camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = orthographicSize;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.82f, 0.84f, 0.88f);
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 30f;
            if (studio)
            {
                _lightGo = new GameObject("ProbeLight");
                var light = _lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                _lightGo.transform.rotation = Quaternion.Euler(45f, 30f, 0f);
                _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                _ground.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
                Object.DestroyImmediate(_ground.GetComponent<Collider>());
                _obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _obstacle.name = "ProbeObstacle";
                Object.DestroyImmediate(_obstacle.GetComponent<Collider>());
                _obstacle.SetActive(false);
            }
            _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _marker.name = "ProbeMarker";
            Object.DestroyImmediate(_marker.GetComponent<Collider>());
            _marker.transform.localScale = Vector3.one * 0.08f;
            _markerMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color")) { color = Color.red };
            if (_markerMaterial.HasProperty("_BaseColor")) _markerMaterial.SetColor("_BaseColor", Color.red);
            _marker.GetComponent<Renderer>().sharedMaterial = _markerMaterial;
            _marker.SetActive(false);
            _rt = new RenderTexture(cellWidth, cellHeight, 24, RenderTextureFormat.ARGB32);
            _cam.targetTexture = _rt;
            _cell = new Texture2D(cellWidth, cellHeight, TextureFormat.RGB24, false);
        }

        /// <summary>Starts a new sheet of <paramref name="columns"/> × <paramref name="rows"/> cells.</summary>
        public void Begin(int columns, int rows)
        {
            if (_sheet != null) Object.DestroyImmediate(_sheet);
            _rows = rows;
            _sheet = new Texture2D(_cellWidth * columns, _cellHeight * rows, TextureFormat.RGB24, false);
        }

        /// <summary>
        /// Renders only what lies between <paramref name="near"/> and <paramref name="far"/> m from the
        /// camera (8 m from the focus): a slice of the scene, so other lanes do not hide the body.
        /// </summary>
        public void SetDepthRange(float near, float far)
        {
            _cam.nearClipPlane = near;
            _cam.farClipPlane = far;
        }

        /// <summary>Shows a box (world center, size and yaw) in the following captures; null hides it (studio setup only).</summary>
        public void SetObstacle(Vector3? center, Vector3 size, float yaw)
        {
            if (_obstacle == null) return;
            _obstacle.SetActive(center.HasValue);
            if (!center.HasValue) return;
            _obstacle.transform.SetPositionAndRotation(center.Value, Quaternion.Euler(0f, yaw, 0f));
            _obstacle.transform.localScale = size;
        }

        /// <summary>Shows a small red sphere at <paramref name="position"/> in the following captures (a contact point); null hides it.</summary>
        public void SetMarker(Vector3? position)
        {
            _marker.SetActive(position.HasValue);
            if (position.HasValue) _marker.transform.position = position.Value;
        }

        /// <summary>
        /// Renders the cell (row, column) looking along <paramref name="view"/> at <paramref name="focus"/>,
        /// with the ground plane at <paramref name="floorY"/>.
        /// </summary>
        public void Capture(int row, int column, Vector3 focus, Vector3 view, float floorY)
        {
            view.Normalize();
            _camGo.transform.SetPositionAndRotation(focus - view * 8f, Quaternion.LookRotation(view, Vector3.up));
            if (_ground != null) _ground.transform.position = new Vector3(focus.x, floorY, focus.z);

            _cam.Render();
            RenderTexture.active = _rt;
            _cell.ReadPixels(new Rect(0, 0, _cellWidth, _cellHeight), 0, 0);
            _cell.Apply();
            RenderTexture.active = null;
            _sheet.SetPixels(column * _cellWidth, (_rows - 1 - row) * _cellHeight, _cellWidth, _cellHeight, _cell.GetPixels());
        }

        public void Save(string path)
        {
            _sheet.Apply();
            File.WriteAllBytes(path, _sheet.EncodeToPNG());
        }

        public void Dispose()
        {
            _cam.targetTexture = null;
            Object.DestroyImmediate(_rt);
            Object.DestroyImmediate(_cell);
            if (_sheet != null) Object.DestroyImmediate(_sheet);
            if (_obstacle != null) Object.DestroyImmediate(_obstacle);
            Object.DestroyImmediate(_marker);
            Object.DestroyImmediate(_markerMaterial);
            if (_ground != null) Object.DestroyImmediate(_ground);
            if (_lightGo != null) Object.DestroyImmediate(_lightGo);
            Object.DestroyImmediate(_camGo);
        }
    }
}
