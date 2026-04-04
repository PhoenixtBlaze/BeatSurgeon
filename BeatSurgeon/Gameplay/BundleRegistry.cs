using System;

namespace BeatSurgeon.Gameplay
{
    internal static class BundleRegistry
    {
        // ── PREFABS (top-level, loaded via AssetBundleMap)
        public const string PrefabAssetBundleMap    = "assets/prefabs/assetbundlemap.prefab";
        public const string PrefabSurgeonExplosion  = "assets/prefabs/surgeonexplosion.prefab";
        public const string PrefabTwitchController  = "assets/prefabs/twitchcontroller.prefab";

        // ── MATERIALS (kept for reference)
        public const string Mat10000BitAtlas         = "assets/material/10000bitatlas.mat";
        public const string Mat1000BitAtlas          = "assets/material/1000bitatlas.mat";
        public const string Mat100BitAtlas           = "assets/material/100bitatlas.mat";
        public const string Mat1BitAtlas             = "assets/material/1bitatlas.mat";
        public const string Mat5000BitAtlas          = "assets/material/5000bitatlas.mat";
        public const string MatBeonSdf               = "assets/material/beon sdf material.mat";
        public const string MatBitsHyperCube         = "assets/material/bitshypercube.mat";
        public const string MatBombNote              = "assets/material/bombnote.mat";
        public const string MatFireworkFlame         = "assets/material/firework_flame_mat.mat";
        public const string MatFireworkHeart         = "assets/material/firework_heart_mat.mat";
        public const string MatFireworkSparkle       = "assets/material/firework_sparkle_mat.mat";
        public const string MatFlameMat              = "assets/material/flame.mat";
        public const string MatFlamePng              = "assets/material/flame.png";
        public const string MatGlitterNote           = "assets/material/glitternote.mat";
        public const string MatGlowColorInstanced    = "assets/material/glowcolorinstanced.mat";
        public const string MatHeartMat              = "assets/material/heart.mat";
        public const string MatHeartPng              = "assets/material/heart.png";
        public const string MatHidden                = "assets/material/hidden.mat";
        public const string MatNoGlowSprite          = "assets/material/noglowsprite.mat";
        public const string MatNote                  = "assets/material/note.mat";
        public const string MatNoteCircle            = "assets/material/notecircle.mat";
        public const string MatSaberBurnMarkCenter   = "assets/material/saberburnmarkcenter.mat";
        public const string MatSaberBurnMarkSparkle  = "assets/material/saberburnmarksparkle.mat";
        public const string MatSparkleCopy           = "assets/material/sparkle - copy.mat";
        public const string MatSparkle               = "assets/material/sparkle.mat";
        public const string MatSparklePng            = "assets/material/sparkle.png";
        public const string MatSubHyperCube          = "assets/material/subhypercube.mat";
        public const string MatTekoMediumSdf         = "assets/material/teko-medium sdf material.mat";
        public const string MatTrail                 = "assets/material/trailmaterial.mat";

        // ── MESHES
        public const string MeshArrow                = "assets/mesh/arrow.asset";
        public const string MeshBomb                 = "assets/mesh/bomb.asset";
        public const string MeshCube010              = "assets/mesh/cube_010.asset";
        public const string MeshCubeNoteSmooth       = "assets/mesh/cubenotesmooth.asset";
        public const string MeshCubeNoteSmoothHD     = "assets/mesh/cubenotesmoothhd.asset";

        internal static class TwitchControllerRefs
        {
            public const string OutlineNodeName = "Outline";
            public const string OutlineEmitterName = "OutlineParticles";
            public const string OutlineSubEmitterName = "SubEmittor";
            public const string BitsHyperCubeNodeName = "BitsHyperCube";
            public const string SubHyperCubeNodeName = "SubHyperCube";
            public const string TrailNodeName = "TrailCube";
            public const string BitsBurstEmitterName = "BitsHyperCubeBurst";
            public const string SubBurstEmitterName = "SubHyperCubeBurst";
            public const string BitsOutlineEmitterPath = "Bits/BitsHyperCube/Outline/OutlineParticles";
            public const string BitsOutlineRootPath = "Bits/BitsHyperCube/Outline";

            public static readonly string[] OutlineMeshCandidates =
            {
                MeshCubeNoteSmoothHD,
                MeshCubeNoteSmooth,
                MeshCube010
            };

            public static readonly string[] OutlineMaterialCandidates =
            {
                MatBitsHyperCube,
                MatSubHyperCube
            };

            public static readonly string[] OutlineParticleMaterialCandidates =
            {
                MatSparkle,
                MatSparkleCopy,
                MatFireworkSparkle
            };

            public static readonly string[] OutlineSubEmitterMaterialCandidates =
            {
                MatSaberBurnMarkCenter,
                MatSaberBurnMarkSparkle
            };

            public static readonly string[] OptionalReferenceMaterialCandidates =
            {
                MatBitsHyperCube,
                MatSubHyperCube,
                MatSparkle,
                MatSparkleCopy,
                MatSaberBurnMarkCenter,
                MatSaberBurnMarkSparkle,
                MatTrail
            };

            public static readonly string[] EmitterAndSubEmitterNames =
            {
                OutlineEmitterName,
                OutlineSubEmitterName,
                BitsBurstEmitterName,
                SubBurstEmitterName
            };
        }

        // ── SHADERS, SPRITES, TEXTURES (omitted for brevity in code references)
    }
}
