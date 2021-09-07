using Mutagen.Bethesda.WPF.Reflection.Attributes;

namespace KnowYourEnemyMutagen
{
    public class Settings
    {
        [SettingName("Intensité de l'effet")]
        public float EffectIntensity { get; set; } = 1.0f;
        
        [SettingName("Modifier l'atout 'Argent'")]
        public bool PatchSilverPerk { get; set; } = false;
    }
}
