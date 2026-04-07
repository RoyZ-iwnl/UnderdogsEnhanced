using System;
using MelonLoader;

namespace UnderdogsEnhanced
{
    internal abstract class UEResourceModule
    {
        private bool staticAssetsLoaded = false;
        private bool dynamicAssetsLoaded = false;

        internal string Id { get; private set; }
        internal bool StaticAssetsLoaded => staticAssetsLoaded;
        internal bool DynamicAssetsLoaded => dynamicAssetsLoaded;

        protected UEResourceModule(string id)
        {
            Id = id;
        }

        internal bool TryLoadStaticAssets()
        {
            if (staticAssetsLoaded) return false;

            try
            {
                LoadStaticAssets();
                staticAssetsLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Assets] Static resource load failed: {Id} | {ex}");
                return false;
            }
        }

        internal bool TryLoadDynamicAssets()
        {
            if (dynamicAssetsLoaded) return false;

            try
            {
                LoadDynamicAssets();
                dynamicAssetsLoaded = true;
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Assets] Dynamic resource load failed: {Id} | {ex}");
                return false;
            }
        }

        internal bool TryUnloadDynamicAssets()
        {
            if (!dynamicAssetsLoaded) return false;

            try
            {
                UnloadDynamicAssets();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Assets] Dynamic resource unload failed: {Id} | {ex}");
            }
            finally
            {
                dynamicAssetsLoaded = false;
            }

            return true;
        }

        protected virtual void LoadStaticAssets() { }
        protected virtual void LoadDynamicAssets() { }
        protected virtual void UnloadDynamicAssets() { }
    }
}
