using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Media3D;

namespace SkyrimOutfit
{
    internal sealed class PoseRig
    {
        private const string FemaleHkx = @"meshes\actors\character\character assets female\skeleton_female.hkx";
        private const string FemaleNif = @"meshes\actors\character\character assets female\skeleton_female.nif";
        private const string MaleHkx = @"meshes\actors\character\character assets\skeleton.hkx";
        private readonly List<Skeleton> skeletons = new List<Skeleton>();
        private readonly Dictionary<string, string> parents;

        internal PoseRig(Catalog catalog)
        {
            string animationArchive = Path.Combine(catalog.DataRoot, "Skyrim - Animations.bsa");
            if (File.Exists(animationArchive))
            {
                try
                {
                    var vanilla = Bsa.LoadFiles(animationArchive, file => Files.Comparer.Equals(file, FemaleHkx) ||
                        Files.Comparer.Equals(file, MaleHkx));
                    foreach (var entry in vanilla.OrderByDescending(entry => Files.Comparer.Equals(entry.Key, FemaleHkx))) AddSkeleton(entry.Value);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            foreach (var source in new[] { Path.Combine(catalog.DataRoot, FemaleHkx),
                Path.Combine(catalog.BodyRoot, @"res\skeleton_female_sse.hkx"),
                Path.Combine(catalog.BodyRoot, @"res\skeleton_female_sk.hkx") }.Where(File.Exists))
            {
                try { AddSkeleton(File.ReadAllBytes(source)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            parents = new Dictionary<string, string>(Files.Comparer);
            foreach (var source in new[] { Path.Combine(catalog.DataRoot, FemaleNif),
                Path.Combine(catalog.BodyRoot, @"res\skeleton_female_sse.nif") }.Where(File.Exists))
            {
                try
                {
                    parents = new Nif(source, File.ReadAllBytes(source), false, readParents: true).BoneParents;
                    if (parents.Count > 0) break;
                }
                catch (InvalidDataException) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private void AddSkeleton(byte[] bytes)
        {
            try
            {
                var file = new HkxPack(bytes);
                foreach (int entry in file.Objects("hkaSkeleton"))
                {
                    int pointer = file.PointerSize, size = file.ArraySize;
                    int header = entry + file.ObjectBase;
                    string name = file.Text(header);
                    if (!name.StartsWith("NPC Root", StringComparison.OrdinalIgnoreCase)) continue;
                    int n, references, bones;
                    int order = file.Array(header + pointer, 2, 1024, out n);
                    int names = file.Array(header + pointer + size, pointer * 2, 1024, out bones);
                    int rest = file.Array(header + pointer + 2 * size, 48, 1024, out references);
                    if (n != bones || n != references || n < 30) continue;
                    var joints = new Joint[n];
                    for (int i = 0; i < n; i++)
                    {
                        int parent = file.Index(order + i * 2);
                        if (parent >= i || parent < -1) throw new InvalidDataException("Unsupported HKX skeleton hierarchy.");
                        int bone = names + i * pointer * 2;
                        var transform = file.Transform(rest + 48 * i);
                        Matrix3D global = transform.Matrix(transform.Position);
                        if (parent >= 0) global.Append(joints[parent].Rest);
                        var inverse = global;
                        if (!inverse.HasInverse) throw new InvalidDataException("Invalid HKX rest pose.");
                        inverse.Invert();
                        joints[i] = new Joint { Name = file.Text(bone), Parent = parent, Rest = global, Inverse = inverse,
                            Local = transform, LockTranslation = file.Byte(bone + pointer) != 0 };
                    }
                    skeletons.Add(new Skeleton { Joints = joints });
                }
            }
            catch (InvalidDataException) { }
        }

        internal PoseModel Attach(HkxClip clip, PreviewGeometry[] shapes)
        {
            if (!String.IsNullOrEmpty(clip.SkeletonName) && !clip.SkeletonName.StartsWith("NPC Root", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This pose uses a different skeleton.");
            var named = new Dictionary<string, int>(Files.Comparer);
            for (int i = 0; i < clip.TrackCount; i++)
                if (!String.IsNullOrWhiteSpace(clip.TrackNames[i]) && !named.ContainsKey(clip.TrackNames[i]))
                    named.Add(clip.TrackNames[i], i);
            int largestIndex = clip.TrackToBone.Length == clip.TrackCount ? clip.TrackToBone.Max(i => (int)i) : clip.TrackCount - 1;
            bool useNames = named.Count >= 10;
            var skeleton = useNames
                ? skeletons.OrderByDescending(item => item.Joints.Count(joint => named.ContainsKey(joint.Name))).FirstOrDefault()
                : skeletons.Where(item => item.Joints.Length > largestIndex)
                    .OrderBy(item => item.Joints.Length - largestIndex).FirstOrDefault();
            if (skeleton == null || (!useNames && clip.TrackToBone.Length == 0 && skeleton.Joints.Length - clip.TrackCount > 5))
                throw new InvalidDataException("No compatible skeleton was found for this animation.");
            var joints = skeleton.Joints;
            var trackByJoint = Enumerable.Repeat(-1, joints.Length).ToArray();
            if (useNames)
            {
                for (int i = 0; i < joints.Length; i++)
                {
                    int track;
                    if (named.TryGetValue(joints[i].Name, out track)) trackByJoint[i] = track;
                }
            }
            else
            {
                for (int track = 0; track < clip.TrackCount; track++)
                {
                    int joint = clip.TrackToBone.Length == clip.TrackCount ? clip.TrackToBone[track] : track;
                    if (joint >= 0 && joint < joints.Length) trackByJoint[joint] = track;
                }
            }
            var jointByName = new Dictionary<string, int>(Files.Comparer);
            for (int i = 0; i < joints.Length; i++)
                if (!String.IsNullOrEmpty(joints[i].Name)) jointByName[joints[i].Name] = i;
            var meshes = shapes.Select(shape => new SkinnedShape { Geometry = shape, Joints = shape.Skin == null ? null :
                shape.Skin.Select(bone => ResolveBone(bone.Name, jointByName)).ToArray() }).ToArray();
            var matched = meshes.Where(mesh => mesh.Joints != null).SelectMany(mesh => mesh.Joints)
                .Where(index => index >= 0 && trackByJoint[index] >= 0).Distinct().Count();
            if (matched < 5) throw new InvalidDataException("This animation does not match the selected body's bones.");
            return new PoseModel(clip, joints, trackByJoint, meshes);
        }

        private int ResolveBone(string bone, Dictionary<string, int> joints)
        {
            for (int i = 0; bone != null && i < 128; i++)
            {
                int index;
                if (joints.TryGetValue(bone, out index)) return index;
                string parent;
                bone = parents.TryGetValue(bone, out parent) ? parent : null;
            }
            return -1;
        }

        private sealed class Skeleton
        {
            internal Joint[] Joints;
        }

        internal sealed class Joint
        {
            internal string Name;
            internal int Parent;
            internal bool LockTranslation;
            internal HkxTransform Local;
            internal Matrix3D Rest, Inverse;
        }

        internal sealed class SkinnedShape
        {
            internal PreviewGeometry Geometry;
            internal int[] Joints;
        }

        internal sealed class PoseModel
        {
            private readonly HkxClip clip;
            private readonly Joint[] joints;
            private readonly int[] trackByJoint;
            private readonly SkinnedShape[] meshes;
            internal int Frames { get { return clip.Frames; } }
            internal double Duration { get { return clip.Duration; } }
            internal PreviewGeometry[] Shapes { get { return meshes.Select(mesh => mesh.Geometry).ToArray(); } }

            internal PoseModel(HkxClip clip, Joint[] joints, int[] tracks, SkinnedShape[] meshes)
            {
                this.clip = clip;
                this.joints = joints;
                this.trackByJoint = tracks;
                this.meshes = meshes;
            }

            internal Point3D[][] Sample(double frame)
            {
                var transforms = clip.Sample(frame);
                var posed = new Matrix3D[joints.Length];
                var skin = new Matrix3D[joints.Length];
                for (int i = 0; i < joints.Length; i++)
                {
                    var joint = joints[i];
                    int track = trackByJoint[i];
                    var motion = track < 0 ? joint.Local : transforms[track];
                    var translation = joint.LockTranslation ? joint.Local.Position : motion.Position;
                    Matrix3D matrix;
                    if (clip.Additive && track >= 0)
                    {
                        matrix = motion.Matrix(joint.LockTranslation ? new Vector3D() : motion.Position);
                        matrix.Append(joint.Local.Matrix(joint.Local.Position));
                    }
                    else matrix = motion.Matrix(translation);
                    if (joint.Parent >= 0) matrix.Append(posed[joint.Parent]);
                    posed[i] = matrix;
                    skin[i] = joint.Inverse;
                    skin[i].Append(matrix);
                }
                return meshes.Select(mesh => Transform(mesh, skin)).ToArray();
            }

            private static Point3D[] Transform(SkinnedShape mesh, Matrix3D[] skin)
            {
                var geometry = mesh.Geometry;
                var positions = geometry.Positions;
                if (mesh.Joints == null) return positions.ToArray();
                var result = new Vector3D[positions.Count];
                var total = new double[result.Length];
                for (int i = 0; i < geometry.Skin.Length; i++)
                {
                    int joint = mesh.Joints[i];
                    if (joint < 0) continue;
                    Matrix3D transform = skin[joint];
                    foreach (var influence in geometry.Skin[i].Weights)
                    {
                        int vertex = influence.Vertex;
                        result[vertex] += (Vector3D)transform.Transform(positions[vertex]) * influence.Weight;
                        total[vertex] += influence.Weight;
                    }
                }
                var output = new Point3D[result.Length];
                for (int i = 0; i < output.Length; i++)
                {
                    double weight = total[i];
                    output[i] = (Point3D)(weight > 1 ? result[i] / weight :
                        result[i] + (Vector3D)positions[i] * (1 - weight));
                }
                return output;
            }
        }
    }
}
