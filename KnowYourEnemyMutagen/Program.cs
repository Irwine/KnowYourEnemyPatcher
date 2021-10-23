using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Strings;
using Newtonsoft.Json.Linq;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Newtonsoft.Json;
using Noggog;
using System.IO;
using System.Threading.Tasks;

namespace KnowYourEnemyMutagen
{
    public static class Program
    {
        private static Lazy<Settings> _settings = null!;
        public static Task<int> Main(string[] args)
        {
            return SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(nickname: "Settings", path: "settings.json", out _settings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "KnowYourEnemyPatcher.esp")
                .Run(args);
        }

        private static float AdjustDamageMod(float magnitude, float scale)
        {
            if (magnitude.EqualsWithin(0))
                return magnitude;
            if (magnitude > 1)
                return (magnitude - 1) * scale + 1;
            return 1 / AdjustDamageMod(1 / magnitude, scale);
        }

        private static float AdjustMagicResist(float magnitude, float scale)
        {
            return magnitude == 0 ? magnitude : magnitude * scale;
        }

        private static readonly (IFormLinkGetter<IPerkGetter> Link, string Keywords)[] PerkArray = {
            (KnowYourEnemy.Perk.kye_perk_fat, "fat"),
            (KnowYourEnemy.Perk.kye_perk_big, "big"),
            (KnowYourEnemy.Perk.kye_perk_small, "small"),
            (KnowYourEnemy.Perk.kye_perk_armored, "armored"),
            (KnowYourEnemy.Perk.kye_perk_undead, "undead"),
            (KnowYourEnemy.Perk.kye_perk_plant, "plant"),
            (KnowYourEnemy.Perk.kye_perk_skeletal, "skeletal"),
            (KnowYourEnemy.Perk.kye_perk_brittle, "brittle"),
            (KnowYourEnemy.Perk.kye_perk_dwarven_machine, "dwarven machine"),
            (KnowYourEnemy.Perk.kye_perk_ghostly, "ghostly"),
            (KnowYourEnemy.Perk.kye_perk_furred, "furred"),
            (KnowYourEnemy.Perk.kye_perk_supernatural, "supernatural"),
            (KnowYourEnemy.Perk.kye_perk_venomous, "venomous"),
            (KnowYourEnemy.Perk.kye_perk_ice_elemental, "ice elemental"),
            (KnowYourEnemy.Perk.kye_perk_fire_elemental, "fire elemental"),
            (KnowYourEnemy.Perk.kye_perk_shock_elemental, "shock elemental"),
            (KnowYourEnemy.Perk.kye_perk_vile, "vile"),
            (KnowYourEnemy.Perk.kye_perk_troll_kin, "troll kin"),
            (KnowYourEnemy.Perk.kye_perk_weak_willed, "weak willed"),
            (KnowYourEnemy.Perk.kye_perk_strong_willed, "strong willed"),
            (KnowYourEnemy.Perk.kye_perk_cave_dwelling, "cave dwelling"),
            (KnowYourEnemy.Perk.kye_perk_vascular, "vascular"),
            (KnowYourEnemy.Perk.kye_perk_aquatic, "aquatic"),
            (KnowYourEnemy.Perk.kye_perk_rocky, "rocky"),
            (KnowYourElements.Perk.kye_perk_earth_elemental, "earth elemental"),
            (KnowYourElements.Perk.kye_perk_water_elemental, "water elemental"),
            (KnowYourElements.Perk.kye_perk_wind_elemental, "wind elemental"),
            (LightAndShadow.Perk.kye_perk_dark_elemental, "dark elemental")
        };

        private static IEnumerable<string> GetFromJson(string key, JObject jObject)
        {
            return jObject.ContainsKey(key) ? jObject[key]!.Select(x => (string?)x).Where(x => x != null).Select(x => x!).ToList() : new List<string>();
        }

        private static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (!state.LoadOrder.ContainsKey(KnowYourEnemy.ModKey))
                throw new Exception("ERROR: Know Your Enemy not detected in load order. You need to install KYE prior to running this patcher!");

            var creatureRulesPath = Path.Combine(state.ExtraSettingsDataPath, "creature_rules.json");
            var miscPath = Path.Combine(state.ExtraSettingsDataPath, "misc.json");
            //var settingsPath = Path.Combine(state.ExtraSettingsDataPath, "settings.json");
            bool failed = false;
            foreach (var f in creatureRulesPath.AsEnumerable()
                .And(miscPath)
                /*.And(settingsPath)*/)
            {
                if (!File.Exists(f))
                {
                    failed = true;
                    Console.Error.WriteLine($"ERROR: Missing required file {f}");
                }
            }
            if (failed)
            {
                throw new Exception($"Missing required files in {state.ExtraSettingsDataPath}! Make sure to copy all files over when installing the patcher, and don't run it from within an archive.");
            }
            // Retrieve all the perks that are going to be applied to NPCs in part 5
            Dictionary<string, IFormLinkGetter<IPerkGetter>> perks = PerkArray
                .Where(x =>
                {
                    return state.LoadOrder.ContainsKey(x.Link.FormKey.ModKey);
                })
                .ToDictionary(x => x.Keywords, x => x.Link, StringComparer.OrdinalIgnoreCase);

            // Reading JSON and converting it to a normal list because .Contains() is weird in Newtonsoft.JSON
            JObject misc = JObject.Parse(File.ReadAllText(miscPath));
            //JObject settings = JObject.Parse(File.ReadAllText(settingsPath));
            var EffectIntensity = _settings.Value.EffectIntensity;
            var PatchSilverPerk = _settings.Value.PatchSilverPerk;
            Console.WriteLine("*** DETECTED SETTINGS ***");
            Console.WriteLine("patch_silver_perk: " + PatchSilverPerk);
            Console.WriteLine("effect_intensity: " + EffectIntensity);
            Console.WriteLine("Light and Shadow detected: " + state.LoadOrder.ContainsKey(LightAndShadow.ModKey));
            Console.WriteLine("Know Your Elements detected: " + state.LoadOrder.ContainsKey(KnowYourElements.ModKey));
            Console.WriteLine("*************************");

            List<string> resistancesAndWeaknesses = GetFromJson("resistances_and_weaknesses", misc).ToList();
            List<string> abilitiesToClean = GetFromJson("abilities_to_clean", misc).ToList();
            List<string> perksToClean = GetFromJson("perks_to_clean", misc).ToList();
            List<string> kyePerkNames = GetFromJson("kye_perk_names", misc).ToList();
            List<string> kyeAbilityNames = GetFromJson("kye_ability_names", misc).ToList();

            Dictionary<string, string[]> creatureRules = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText(creatureRulesPath))!;

            // Part 1a
            // Removing other magical resistance/weakness systems
            foreach (var spell in state.LoadOrder.PriorityOrder.WinningOverrides<ISpellGetter>())
            {
                if (spell.EditorID == null || !abilitiesToClean.Contains(spell.EditorID)) continue;
                var modifiedSpell = spell.DeepCopy();
                bool spellModified = false;
                foreach (var effect in modifiedSpell.Effects)
                {
                    effect.BaseEffect.TryResolve(state.LinkCache, out var baseEffect);
                    if (baseEffect?.EditorID == null) continue;
                    if (!resistancesAndWeaknesses.Contains(baseEffect.EditorID)) continue;
                    if (effect.Data != null)
                    {
                        effect.Data.Magnitude = 0;
                        spellModified = true;
                    }
                    else
                        Console.WriteLine("Error setting Effect Magnitude - DATA was null!");
                }
                if (spellModified) {
                    if (modifiedSpell.Name != null && modifiedSpell.Name.TryLookup(Language.French, out string i18nSpellName)) {
                        modifiedSpell.Name = i18nSpellName;
                    }
                    state.PatchMod.Spells.Set(modifiedSpell);
				}
            }

            // Part 1b
            // Remove other weapon resistance systems
            foreach (var perk in state.LoadOrder.PriorityOrder.WinningOverrides<IPerkGetter>())
            {
                if (perk.EditorID == null || !perksToClean.Contains(perk.EditorID)) continue;
                foreach (var eff in perk.Effects)
                {
                    if (!(eff is PerkEntryPointModifyValue modValue)) continue;
                    if (modValue.EntryPoint != APerkEntryPointEffect.EntryType.ModIncomingDamage) continue;
                    modValue.Value = 1f;
                    modValue.Modification = PerkEntryPointModifyValue.ModificationType.Set;
                }
            }

            // Part 2a
            // Adjust KYE's physical effects according to effect_intensity
            if (!EffectIntensity.EqualsWithin(1))
            {
                foreach (var perk in state.LoadOrder.PriorityOrder.WinningOverrides<IPerkGetter>())
                {
                    bool perkModified = false;
                    if (perk.EditorID == null || !kyePerkNames.Contains(perk.EditorID) || !perk.Effects.Any()) continue;
                    Perk perkCopy = perk.DeepCopy();
                    foreach (var eff in perkCopy.Effects)
                    {
                        if (eff is not PerkEntryPointModifyValue modValue) continue;
                        if (modValue.EntryPoint == APerkEntryPointEffect.EntryType.ModIncomingDamage || modValue.EntryPoint == APerkEntryPointEffect.EntryType.ModAttackDamage)
                        {
                            var currentMagnitude = modValue.Value ?? 0;
                            modValue.Value = AdjustDamageMod(currentMagnitude, EffectIntensity);
                            modValue.Modification = PerkEntryPointModifyValue.ModificationType.Multiply;
                            perkModified = true;
                        }
                    }
                    if (perkModified) state.PatchMod.Perks.Set(perkCopy);
                }

                // Part 2b
                // Adjust KYE's magical effects according to effect_intensity

                foreach (var spell in state.LoadOrder.PriorityOrder.WinningOverrides<ISpellGetter>())
                {
                    if (spell.EditorID == null || !kyeAbilityNames.Contains(spell.EditorID)) continue;
                    Spell s = spell.DeepCopy();
					
                    if (s.Name != null && s.Name.TryLookup(Language.French, out string i18nSpellName)) {
                        s.Name = i18nSpellName;
                    }

                    foreach (var eff in s.Effects)
                    {
                        eff.BaseEffect.TryResolve(state.LinkCache, out var baseEffect);
                        if (baseEffect?.EditorID == null
                            || !resistancesAndWeaknesses.Contains(baseEffect.EditorID)
                            || eff.Data == null) continue;
                        var currentMagnitude = eff.Data.Magnitude;
                        eff.Data.Magnitude = AdjustMagicResist(currentMagnitude, EffectIntensity);
                        state.PatchMod.Spells.Set(s);
                    }
                }
            }

            // Part 3
            // Edit the effect of silver weapons

            if (PatchSilverPerk)
            {
                if (state.LoadOrder.ContainsKey(ModKey.FromNameAndExtension("Skyrim Immersive Creatures.esp")))
                    Console.WriteLine("WARNING: Silver Perk is being patched, but Skyrim Immersive Creatures has been detected in your load order. Know Your Enemy's silver weapon effects will NOT work against new races added by SIC.");

                if (Skyrim.Perk.SilverPerk.TryResolve(state.LinkCache, out var silverPerk))
                {
                    if (KnowYourEnemy.Perk.DummySilverPerk.TryResolve(state.LinkCache, out var dummySilverPerk))
                    {
                        Perk kyePerk = silverPerk.DeepCopy();
                        kyePerk.Effects.Clear();
                        foreach (var aPerkEffectGetter in dummySilverPerk.Effects)
                        {
                            var eff = (APerkEffect)aPerkEffectGetter;
                            kyePerk.Effects.Add(eff);
                        }

                        state.PatchMod.Perks.GetOrAddAsOverride(kyePerk);
                    }
                }
            }

            // Part 4
            // Adjust traits to accommodate CACO if present
            if (state.LoadOrder.ContainsKey(ModKey.FromNameAndExtension("Complete Alchemy & Cooking Overhaul.esp")))
            {
                Console.WriteLine("CACO detected! Adjusting kye_ab_undead and kye_ab_ghostly spells.");
                if (KnowYourEnemy.Spell.kye_ab_ghostly.TryResolve(state.LinkCache, out var kyeAbGhostly))
                {
                    Spell kyeAbGhostlyCaco = kyeAbGhostly.DeepCopy();
					
                    if (kyeAbGhostlyCaco.Name != null && kyeAbGhostlyCaco.Name.TryLookup(Language.French, out string i18nSpellName)) {
                        kyeAbGhostlyCaco.Name = i18nSpellName;
                    }
                    
                    foreach (var eff in kyeAbGhostlyCaco.Effects)
                    {
                        if (eff.Data == null) continue;
                        if (!eff.BaseEffect.TryResolve(state.LinkCache, out var baseEffect)) continue;
                        if (!baseEffect.Equals(Skyrim.MagicEffect.AbResistPoison)) continue;
                        eff.Data.Magnitude = 0;
                        state.PatchMod.Spells.GetOrAddAsOverride(kyeAbGhostlyCaco);
                    }
                }
                else
                {
                    Console.WriteLine($"WARNING! CACO detected but failed to patch kye_ab_ghostly_caco spell. Do you have {KnowYourEnemy.ModKey} active in the load order?");
                }

                if (KnowYourEnemy.Spell.kye_ab_undead.TryResolve(state.LinkCache, out var kyeAbUndead))
                {
                    Spell kyeAbUndeadCaco = kyeAbUndead.DeepCopy();
					
                    if (kyeAbUndeadCaco.Name != null && kyeAbUndeadCaco.Name.TryLookup(Language.French, out string i18nSpellName)) {
                        kyeAbUndeadCaco.Name = i18nSpellName;
                    }
                    
                    foreach (var eff in kyeAbUndeadCaco.Effects)
                    {
                        if (eff.Data == null) continue;
                        if (!eff.BaseEffect.TryResolve(state.LinkCache, out var baseEffect)) continue;
                        if (!baseEffect.Equals(Skyrim.MagicEffect.AbResistPoison)) continue;
                        eff.Data.Magnitude = 0;
                        state.PatchMod.Spells.GetOrAddAsOverride(kyeAbUndeadCaco);
                    }
                }
                else
                {
                    Console.WriteLine($"WARNING! CACO detected but failed to patch kye_ab_undead_caco spell. Do you have {KnowYourEnemy.ModKey} active in the load order?");
                }
            }

            // Part 5
            // Add the traits to NPCs

            foreach (var npc in state.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>())
            {
                // Skip if npc has spell list
                if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.SpellList)) continue;

                var traits = new List<string>();

                // If ghost
                if (npc.Keywords?.Contains(Skyrim.Keyword.ActorTypeGhost) ?? false)
                {
                    if (!traits.Contains("ghostly"))
                        traits.Add("ghostly");
                }

                // If npc race is in creature_rules
                if (npc.Race.TryResolve(state.LinkCache, out var race) && race.EditorID != null && creatureRules!.ContainsKey(race.EditorID))
                {
                    foreach (string trait in creatureRules[race.EditorID])
                    {
                        if (!traits.Contains(trait))
                            traits.Add(trait);
                    }
                }

                // If npc name is in creature_rules
                if (npc.Name != null && creatureRules!.ContainsKey(npc.Name.ToString()!))
                {
                    foreach (string trait in creatureRules[npc.Name.ToString()!])
                    {
                        if (!traits.Contains(trait))
                            traits.Add(trait);
                    }
                }

                // If npc EDID is in creature_rules
                if (npc.EditorID != null && creatureRules!.ContainsKey(npc.EditorID))
                {
                    foreach (string trait in creatureRules[npc.EditorID])
                    {
                        if (!traits.Contains(trait))
                            traits.Add(trait);
                    }
                }

                // If Ice Wraith remove ghostly
                if (npc.Name != null && npc.Name.ToString() == "Ice Wraith")
                {
                    if (traits.Contains("ghostly"))
                        traits.Remove("ghostly");
                }

                // Add perks
                if (traits.Any())
                {
                    Npc kyeNpc = state.PatchMod.Npcs.GetOrAddAsOverride(npc);
					
					if (kyeNpc.Name != null && kyeNpc.Name.TryLookup(Language.French, out string i18nNpcName)) {
                        kyeNpc.Name = i18nNpcName;
                    }
                    if (kyeNpc.ShortName != null && kyeNpc.ShortName.TryLookup(Language.French, out string i18nNpcShortName)) {
                        kyeNpc.ShortName = i18nNpcShortName;
                    }
					
                    if (kyeNpc.Perks == null)
                        kyeNpc.Perks = new ExtendedList<PerkPlacement>();
                    foreach (string trait in traits)
                    {
                        try
                        {
                            PerkPlacement p = new PerkPlacement() { Perk = perks[trait].AsSetter(), Rank = 1 };
                            kyeNpc.Perks.Add(p);
                        }
                        catch (KeyNotFoundException)
                        {
                            Console.WriteLine("Could not add the " + trait + " trait to NPC " + kyeNpc.EditorID + ". You may ignore this warning if you're running Shadow Spell Package without the KYE extension installed.");
                        }
                    }
                    /* For debugging purposes
                    if (npc.Name != null && traits.Any())
                    {
                        Console.WriteLine("NPC " + npc.Name! + " receives traits: " + traits.Count);
                        foreach (string t in traits)
                        {
                            Console.WriteLine(t);
                        }
                    }
                    */
                }
            }
        }
    }
}
