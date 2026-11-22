using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;

namespace SkyrimOutfit
{
    internal sealed class LiveViewport
    {
        private readonly Viewport3D view;
        private readonly PerspectiveCamera camera = new PerspectiveCamera { FieldOfView = 36, NearPlaneDistance = 0.1 };
        private readonly ModelVisual3D model = new ModelVisual3D();
        private readonly Dictionary<string, PreviewGeometry[]> cache = new Dictionary<string, PreviewGeometry[]>(Files.Comparer);
        private PreviewTextures textures = new PreviewTextures();
        private Point dragPoint;
        private Point3D center;
        private Rect3D lastBounds = Rect3D.Empty;
        private double yaw = Math.PI, elevation = 0.07, distance = 320;
        private bool dragging;
        private int revision;
        private MeshGeometry3D[] posedMeshes;

        internal LiveViewport(Viewport3D view, UIElement inputSurface)
        {
            this.view = view;
            view.Camera = camera;
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(118, 124, 132)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(235, 232, 223), new Vector3D(0.9, -1.2, -0.7)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(82, 103, 126), new Vector3D(-1, 1, -0.2)));
            view.Children.Add(new ModelVisual3D { Content = lights });
            view.Children.Add(model);
            inputSurface.MouseLeftButtonDown += (s, e) => {
                dragPoint = e.GetPosition(inputSurface);
                dragging = inputSurface.CaptureMouse();
            };
            inputSurface.MouseLeftButtonUp += (s, e) => {
                dragging = false;
                if (inputSurface.IsMouseCaptured) inputSurface.ReleaseMouseCapture();
            };
            inputSurface.LostMouseCapture += (s, e) => dragging = false;
            inputSurface.MouseMove += (s, e) => {
                if (!dragging) return;
                Point now = e.GetPosition(inputSurface);
                yaw -= (now.X - dragPoint.X) * .012;
                elevation = Math.Max(-1.25, Math.Min(1.25, elevation + (now.Y - dragPoint.Y) * .01));
                dragPoint = now;
                SetCamera();
            };
            inputSurface.MouseWheel += (s, e) => {
                distance = Math.Max(2, Math.Min(5000, distance * Math.Pow(.85, e.Delta / 120.0)));
                SetCamera();
                e.Handled = true;
            };
            view.SizeChanged += (s, e) => FitCamera();
            SetCamera();
        }

        internal async Task Render(Catalog catalog, Piece[] pieces, bool textured, Action<int, int, int> finished)
        {
            int version = ++revision;
            if (catalog == null || pieces.Length == 0)
            {
                model.Content = null;
                lastBounds = Rect3D.Empty;
                finished(0, 0, 0);
                return;
            }
            var pending = await Task.Run(() => ReadMeshes(catalog, pieces, textured));
            await view.Dispatcher.InvokeAsync(() => {
                if (version == revision) Draw(pending.Item1, pending.Item2, textured, finished);
            });
        }

        private Tuple<List<PreviewPart>, int> ReadMeshes(Catalog catalog, Piece[] pieces, bool textured)
        {
            var sources = new List<PreviewPart>();
            int unavailable = 0;
            foreach (Piece piece in pieces)
            {
                string file;
                try { file = piece.FindNif(catalog.BodyRoot); }
                catch (IOException) { unavailable++; continue; }
                PreviewGeometry[] meshes;
                lock (cache) cache.TryGetValue(file, out meshes);
                if (meshes == null)
                {
                    try { meshes = new Nif(file, true).PreviewShapes.ToArray(); }
                    catch (IOException) { unavailable++; continue; }
                    catch (InvalidDataException) { unavailable++; continue; }
                    lock (cache)
                    {
                        if (cache.Count >= 42) cache.Clear();
                        cache[file] = meshes;
                    }
                }
                if (meshes.Length != 0)
                {
                    sources.Add(Prepare(meshes, textures, catalog.DataRoot, piece.ModFolder, textured, null));
                }
                else unavailable++;
            }
            return Tuple.Create(sources, unavailable);
        }

        internal void ResetTextureCache() { textures = new PreviewTextures(); }

        internal async Task RenderBody(BodyPreviewCache bodies, Catalog catalog, Piece piece, BodyPreset preset, bool lowWeight, bool textured,
            CancellationToken token, Action<int, int, int> finished)
        {
            int version = ++revision;
            model.Content = null;
            lastBounds = Rect3D.Empty;
            if (catalog == null || piece == null)
            {
                finished(0, 0, 0);
                return;
            }
            var textureSet = textures;
            var part = await Task.Run(async () => {
                PreviewGeometry[] shapes = await bodies.Get(catalog, piece, preset, lowWeight, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return Prepare(shapes, textureSet, catalog.DataRoot, piece.ModFolder, textured, null);
            }, token);
            await view.Dispatcher.InvokeAsync(() => {
                if (version == revision && !token.IsCancellationRequested)
                    Draw(new List<PreviewPart> { part }, 0, textured, finished);
            });
        }

        internal async Task ShowPose(Catalog catalog, Piece body, PoseRig.PoseModel pose, bool textured, CancellationToken token)
        {
            int version = ++revision;
            var texturesForPose = textures;
            var result = await Task.Run(() => {
                token.ThrowIfCancellationRequested();
                return Tuple.Create(Prepare(pose.Shapes, texturesForPose, catalog.DataRoot, body.ModFolder, textured, null),
                    pose.Sample(0));
            }, token);
            if (version == revision && !token.IsCancellationRequested)
                Draw(new List<PreviewPart> { result.Item1 }, 0, textured, (shown, unavailable, missing) => { }, result.Item2);
        }

        internal void UpdatePoseFrame(Point3D[][] positions)
        {
            if (posedMeshes == null || posedMeshes.Length != positions.Length) return;
            for (int shape = 0; shape < posedMeshes.Length; shape++)
                if (posedMeshes[shape] != null) posedMeshes[shape].Positions = new Point3DCollection(positions[shape]);
        }

        internal void ClearPose()
        {
            revision++;
            model.Content = null;
            posedMeshes = null;
            lastBounds = Rect3D.Empty;
        }

        internal async Task RenderInstalled(InstalledOutfits installed, Catalog catalog, Target target, string stem, bool lowWeight, bool textured,
            Action<InstalledMesh, InstalledSource, int> finished)
        {
            int version = ++revision;
            if (installed == null || catalog == null || target == null || stem == null)
            {
                model.Content = null;
                lastBounds = Rect3D.Empty;
                finished(null, null, 0);
                return;
            }
            var textureSet = textures;
            var result = await Task.Run(() => {
                InstalledMesh mesh = null;
                PreviewPart part = null;
                InstalledSource source = null;
                try
                {
                    mesh = installed.Find(stem, target.Weighted, lowWeight);
                    if (mesh != null)
                    {
                        var shapes = mesh.Read().PreviewShapes.ToArray();
                        part = Prepare(shapes, textureSet, installed.DataRoot, null, textured, installed.Archives);
                        source = installed.Provenance.Describe(mesh, target.Output, catalog, shapes);
                    }
                }
                catch (IOException) { }
                catch (InvalidDataException) { }
                catch (UnauthorizedAccessException) { }
                return Tuple.Create(mesh, part, source);
            });
            if (version != revision) return;
            var sources = new List<PreviewPart>();
            if (result.Item2 != null) sources.Add(result.Item2);
            Draw(sources, 0, textured, (count, unavailable, missing) => finished(result.Item1, result.Item3, count));
        }

        private static PreviewPart Prepare(PreviewGeometry[] meshes, PreviewTextures textures, string data, string mod, bool textured,
            IEnumerable<string> archives)
        {
            BitmapSource[] maps = textured ? meshes.Select(shape => shape.TextureCoordinates.Count == 0
                ? null : textures.Load(data, mod, shape, archives)).ToArray() : null;
            return new PreviewPart { Meshes = meshes, Maps = maps };
        }

        private void Draw(List<PreviewPart> sources, int unavailable, bool textured, Action<int, int, int> finished,
            Point3D[][] posed = null)
        {
            var assembly = new Model3DGroup();
            posedMeshes = posed == null ? null : new MeshGeometry3D[posed.Length];
            var includedBody = new HashSet<string>(Files.Comparer);
            int missingTextures = 0;
            var palette = new[] { Color.FromRgb(150, 160, 171), Color.FromRgb(172, 154, 142),
                Color.FromRgb(134, 160, 159), Color.FromRgb(159, 148, 172), Color.FromRgb(182, 143, 119),
                Color.FromRgb(149, 170, 124), Color.FromRgb(119, 154, 179), Color.FromRgb(182, 132, 152),
                Color.FromRgb(189, 174, 106), Color.FromRgb(136, 136, 188) };
            for (int p = 0; p < sources.Count; p++)
            for (int i = 0; i < sources[p].Meshes.Length; i++)
            {
                var shape = sources[p].Meshes[i];
                string body = Composer.BodyPart(shape.Name);
                if (body != "" && !includedBody.Add(body)) continue;
                if (body == "anus" || body == "vagina") continue;
                var map = textured ? sources[p].Maps[i] : null;
                var mesh = new MeshGeometry3D {
                    Positions = posed == null ? new Point3DCollection(shape.Positions) : new Point3DCollection(posed[i]),
                    TriangleIndices = new Int32Collection(shape.Indices)
                };
                if (map != null) mesh.TextureCoordinates = new PointCollection(shape.TextureCoordinates);
                if (posed == null) mesh.Freeze();
                else posedMeshes[i] = mesh;
                Brush surface;
                if (map != null)
                {
                    surface = new ImageBrush(map) {
                        Stretch = Stretch.Fill, TileMode = TileMode.Tile,
                        ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1)
                    };
                }
                else
                {
                    if (textured && !String.IsNullOrEmpty(shape.DiffuseTexture)) missingTextures++;
                    surface = new SolidColorBrush(textured || body != "" ? Color.FromRgb(119, 119, 115) : palette[p % palette.Length]);
                }
                var material = new DiffuseMaterial(surface);
                material.Freeze();
                assembly.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
            }
            if (posed == null) assembly.Freeze();
            model.Content = assembly;
            lastBounds = assembly.Bounds;
            FitCamera();
            finished(assembly.Children.Count, unavailable, missingTextures);
        }

        private void FitCamera()
        {
            Rect3D bounds = lastBounds;
            if (bounds.IsEmpty) return;
            double aspect = view.ActualHeight > 0 ? Math.Max(1, view.ActualWidth / view.ActualHeight) : 1.7;
            center = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2);
            distance = Math.Max(8, Math.Max(bounds.SizeZ * aspect, Math.Max(bounds.SizeX, bounds.SizeY)) * 1.95);
            SetCamera();
        }

        private void SetCamera()
        {
            double flat = distance * Math.Cos(elevation);
            camera.Position = new Point3D(center.X + flat * Math.Sin(yaw), center.Y - flat * Math.Cos(yaw),
                center.Z + distance * Math.Sin(elevation));
            camera.LookDirection = center - camera.Position;
            camera.UpDirection = new Vector3D(0, 0, 1);
        }

        private sealed class PreviewPart
        {
            internal PreviewGeometry[] Meshes;
            internal BitmapSource[] Maps;
        }
    }
}
