using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Newtonsoft.Json.Linq;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Newtonsoft.Json;
using Noggog;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;

namespace KnowYourEnemyMutagen
{
    public static class Program
    {
        public static Task<int> Main(string[] args)
        {
            return SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "know_your_enemy_patcher.esp")
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

        private static async void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            if (!state.LoadOrder.ContainsKey(KnowYourEnemy.ModKey))
                throw new Exception("ERROR: Know Your Enemy not detected in load order. You need to install KYE prior to running this patcher!");

            var creatureRulesPath = Path.Combine(state.ExtraSettingsDataPath, "creature_rules.json");
            var miscPath = Path.Combine(state.ExtraSettingsDataPath, "misc.json");
            var settingsPath = Path.Combine(state.ExtraSettingsDataPath, "settings.json");
            bool failed = false;
            foreach (var f in creatureRulesPath.AsEnumerable()
                .And(miscPath)
                .And(settingsPath))
            {
                if (!File.Exists(f))
                {
                    failed = true;
                    Console.Error.WriteLine($"ERROR: Missing required file {f}");
                }
            }
            if (failed)
                throw new Exception($"Missing required files in {state.ExtraSettingsDataPath}! Make sure to copy all files over when installing the patcher, and don't run it from within an archive.");

            // Retrieve all the perks that are going to be applied to NPCs in part 5
            Dictionary<string, IFormLinkGetter<IPerkGetter>> perks = PerkArray
                .Where(x =>
                {
                    return state.LoadOrder.ContainsKey(x.Link.FormKey.ModKey);
                })
                .ToDictionary(x => x.Keywords, x => x.Link, StringComparer.OrdinalIgnoreCase);

            // Reading JSON and converting it to a normal list because .Contains() is weird in Newtonsoft.JSON
            JObject misc = JObject.Parse(File.ReadAllText(miscPath));
            JObject settings = JObject.Parse(File.ReadAllText(settingsPath));

            var effectIntensity = (float)settings["effect_intensity"]!;
            var patchSilverPerk = (bool)settings["patch_silver_perk"]!;

            Console.WriteLine("*** DETECTED SETTINGS ***");
            Console.WriteLine("patch_silver_perk: " + patchSilverPerk);
            Console.WriteLine("effect_intensity: " + effectIntensity);
            Console.WriteLine("Light and Shadow detected: " + state.LoadOrder.ContainsKey(LightAndShadow.ModKey));
            Console.WriteLine("Know Your Elements detected: " + state.LoadOrder.ContainsKey(KnowYourElements.ModKey));
            Console.WriteLine("*************************");

            List<string> resistancesAndWeaknesses = GetFromJson("resistances_and_weaknesses", misc).ToList();
            List<string> abilitiesToClean = GetFromJson("abilities_to_clean", misc).ToList();
            List<string> perksToClean = GetFromJson("perks_to_clean", misc).ToList();
            List<string> kyePerkNames = GetFromJson("kye_perk_names", misc).ToList();
            List<string> kyeAbilityNames = GetFromJson("kye_ability_names", misc).ToList();

            Dictionary<string, string[]> creatureRules = JsonConvert.DeserializeObject<Dictionary<string, string[]>>(File.ReadAllText(creatureRulesPath));

            // Part 1a && 1b
            // Removing other magical resistance/weakness systems, remove other weapon resistance systems
            Task magicResistances = RemoveMagicResistancesAsync(state, resistancesAndWeaknesses, abilitiesToClean);

            // Part 1b
            // Remove other weapon resistance systems
            Task weaponResistances = RemoveWeaponResistancesAsync(state, perksToClean);

            // Part 2a
            // Adjust KYE's physical effects according to effect_intensity
            if (!effectIntensity.EqualsWithin(1))
            {
                foreach (var perk in state.LoadOrder.PriorityOrder.Perk().WinningOverrides())
                {
                    bool perkModified = false;
                    if (perk.EditorID == null || !kyePerkNames.Contains(perk.EditorID) || !perk.Effects.Any()) continue;
                    Perk perkCopy = perk.DeepCopy();
                    foreach (var eff in perkCopy.Effects)
                    {
                        if (!(eff is PerkEntryPointModifyValue modValue)) continue;
                        if (modValue.EntryPoint == APerkEntryPointEffect.EntryType.ModIncomingDamage || modValue.EntryPoint == APerkEntryPointEffect.EntryType.ModAttackDamage)
                        {
                            var currentMagnitude = modValue.Value;
                            modValue.Value = AdjustDamageMod(currentMagnitude, effectIntensity);
                            modValue.Modification = PerkEntryPointModifyValue.ModificationType.Multiply;
                            perkModified = true;
                        }
                        else continue;
                    }
                    if (perkModified) state.PatchMod.Perks.Set(perkCopy);
                }

                // Part 2b
                // Adjust KYE's magical effects according to effect_intensity

                foreach (var spell in state.LoadOrder.PriorityOrder.Spell().WinningOverrides())
                {
                    if (spell.EditorID == null || !kyeAbilityNames.Contains(spell.EditorID)) continue;
                    Spell s = spell.DeepCopy();
                    foreach (var eff in s.Effects)
                    {
                        eff.BaseEffect.TryResolve(state.LinkCache, out var baseEffect);
                        if (baseEffect?.EditorID == null
                            || !resistancesAndWeaknesses.Contains(baseEffect.EditorID)
                            || eff.Data == null) continue;
                        var currentMagnitude = eff.Data.Magnitude;
                        eff.Data.Magnitude = AdjustMagicResist(currentMagnitude, effectIntensity);
                        state.PatchMod.Spells.Set(s);
                    }
                }
            }

            // Part 3
            // Edit the effect of silver weapons

            if (patchSilverPerk)
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

                        state.PatchMod.Perks.Set(kyePerk);
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

            foreach (var npc in state.LoadOrder.PriorityOrder.Npc().WinningOverrides())
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
                if (npc.Race.TryResolve(state.LinkCache, out var race) && race.EditorID != null && creatureRules.ContainsKey(race.EditorID))
                {
                    foreach (string trait in creatureRules[race.EditorID])
                    {
                        if (!traits.Contains(trait))
                            traits.Add(trait);
                    }
                }

                // If npc name is in creature_rules
                if (npc.Name != null && creatureRules.ContainsKey(npc.Name.ToString()!))
                {
                    foreach (string trait in creatureRules[npc.Name.ToString()!])
                    {
                        if (!traits.Contains(trait))
                            traits.Add(trait);
                    }
                }

                // If npc EDID is in creature_rules
                if (npc.EditorID != null && creatureRules.ContainsKey(npc.EditorID))
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
                    if (kyeNpc.Perks == null)
                        kyeNpc.Perks = new ExtendedList<PerkPlacement>();
                    foreach (string trait in traits)
                    {
                        try
                        {
                            PerkPlacement p = new PerkPlacement() { Perk = perks[trait].AsSetter(), Rank = 1 };
                            kyeNpc.Perks.Add(p);
                        }
                        catch (KeyNotFoundException e)
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
            await magicResistances;
            await weaponResistances;
            sw.Stop();
            Console.WriteLine($"Patcher took {sw.ElapsedMilliseconds} to complete!");
        }

        private static async Task RemoveWeaponResistancesAsync(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, List<string> perksToClean)
        {
            foreach (var perk in state.LoadOrder.PriorityOrder.Perk().WinningOverrides())
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
        }

        private static async Task RemoveMagicResistancesAsync(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, List<string> resistancesAndWeaknesses, List<string> abilitiesToClean)
        {
            foreach (var spell in state.LoadOrder.PriorityOrder.Spell().WinningOverrides())
            {
                if (spell.EditorID == null || !abilitiesToClean.Contains(spell.EditorID)) continue;
                var modifiedSpell = spell.DeepCopy();
                bool spellModified = false;
                foreach (var effect in modifiedSpell.Effects)
                {
                    effect.BaseEffect.TryResolve(state.LinkCache, out var baseEffect);
                    if (baseEffect?.EditorID == null) continue;
                    if (!resistancesAndWeaknesses.Contains(baseEffect.EditorID)) continue;
                    if (effect.Data == null || effect.Data.Magnitude == 0) continue;

                    effect.Data.Magnitude = 0;
                    spellModified = true;
                }
                if (spellModified) state.PatchMod.Spells.Set(modifiedSpell);
            }
        }
    }
}
