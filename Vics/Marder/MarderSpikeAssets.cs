using UnityEngine;

namespace UnderdogsEnhanced
{
    internal static class MarderSpikeAssets
    {
        internal static GameObject GetThermalPostPrefab()
        {
            return UEResourceController.GetThermalFlirPostPrefab();
        }

        internal static Material CreateConfiguredBlitMaterial()
        {
            Material whiteHotMaterial = UEResourceController.GetThermalFlirWhiteBlitMaterialNoScope();
            if (whiteHotMaterial != null)
            {
                return UEResourceController.CreateThermalMaterial(whiteHotMaterial,
                    new ThermalConfig { ColorMode = ThermalColorMode.WhiteHot });
            }

            Material thermalMaterial = UEResourceController.GetThermalFlirBlitMaterial();
            if (thermalMaterial != null)
            {
                return UEResourceController.CreateThermalMaterial(thermalMaterial,
                    new ThermalConfig { ColorMode = ThermalColorMode.WhiteHot });
            }

            Material noScan = UEResourceController.GetThermalFlirBlitMaterialNoScan();
            return noScan != null ? noScan : thermalMaterial;
        }
    }
}
