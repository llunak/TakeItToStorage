﻿using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ModBase;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace HaulToBuilding
{
    public class HaulToBuildingMod : BaseMod<HaulToBuildingModSettings>
    {
        public HaulToBuildingMod(ModContentPack content) : base("legodude17.HaulToBuilding", null, content)
        {
            Dialog_BillConfig_Patches.DoPatches(Harm);
            Toils_Recipe_Patches.DoPatches(Harm);
            RecipeCountWorker_Patches.DoPatches(Harm);
            Harm.Patch(AccessTools.Method(typeof(Bill), "Clone"),
                postfix: new HarmonyMethod(GetType(), "CloneData"));
            Harm.Patch(AccessTools.Method(typeof(Bill), "ExposeData"),
                postfix: new HarmonyMethod(GetType(), "SaveData"));
            Harm.Patch(AccessTools.Method(typeof(WorkGiver_DoBill), "TryFindBestBillIngredients"),
                new HarmonyMethod(GetType(), "GetIngredients"));
            if (ModLister.HasActiveModWithName("Better Workbench Management"))
            {
                Log.Message("[TakeItToStorage] Better Workbench Management compatibility active");
                Harm.Patch(AccessTools.Method(
                        Type.GetType("ImprovedWorkbenches.ExtendedBillDataStorage, ImprovedWorkbenches"),
                        "MirrorBills"),
                    postfix: new HarmonyMethod(GetType(), "CloneData2"));
            }

            // SettingsRenderer.__DEBUG = true;
        }

        public static void CloneData(Bill __instance, Bill __result)
        {
            var data = GameComponent_ExtraBillData.Instance.GetData(__instance).Clone();
            data.NeedCheck = true;
            GameComponent_ExtraBillData.Instance.SetData(__result, data);
            if (__instance is Bill_Production billProd)
                if (billProd.GetStoreMode() == HaulToBuildingDefOf.StorageBuilding && data.Storage == null ||
                    billProd.GetStoreMode() == BillStoreModeDefOf.SpecificStockpile && billProd.GetStoreZone() == null)
                {
                    Log.Warning("[HaulToBuilding] Inconsistent Bill setting, fixing");
                    billProd.SetStoreMode(BillStoreModeDefOf.BestStockpile);
                }

            if (data.TakeFrom.Any())
                foreach (var parent in data.TakeFrom.Where(parent => parent?.SlotYielderLabel()?.NullOrEmpty() ?? true)
                    .ToList())
                    data.TakeFrom.Remove(parent);
        }

        public static void CloneData2(Bill_Production sourceBill, Bill_Production destinationBill)
        {
            GameComponent_ExtraBillData.Instance.SetData(destinationBill,
                GameComponent_ExtraBillData.Instance.GetData(sourceBill).Clone());
        }

        public static void SaveData(Bill __instance)
        {
            var data = GameComponent_ExtraBillData.Instance.GetData(__instance, false);
            Scribe_Deep.Look(ref data, "htb_extraData");
            if (data != null) GameComponent_ExtraBillData.Instance.SetData(__instance, data);
        }

        public static bool GetIngredients(Bill bill, Pawn pawn, Thing billGiver, List<ThingCount> chosen,
            ref bool __result)
        {
            var extraData = GameComponent_ExtraBillData.Instance.GetData(bill);
            if (!extraData.TakeFrom.Any()) return true;

            var rootCell = WorkGiver_DoBill.GetBillGiverRootCell(billGiver, pawn);

            bool BaseValidator(Thing t)
            {
                return t.Spawned && !t.IsForbidden(pawn) &&
                       (t.Position - billGiver.Position)
                       .LengthHorizontalSquared <
                       bill.ingredientSearchRadius *
                       bill.ingredientSearchRadius &&
                       bill.IsFixedOrAllowedIngredient(t) &&
                       bill.recipe.ingredients.Any(ingNeed =>
                           ingNeed.filter.Allows(t)) &&
                       pawn.CanReserve(t) &&
                       pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly) &&
                       t.def.EverHaulable;
            }

            WorkGiver_DoBill.relevantThings.AddRange(extraData.TakeFrom
                .SelectMany(parent => parent.GetSlotGroup().HeldThings).Where(BaseValidator));
            __result = WorkGiver_DoBill.TryFindBestBillIngredientsInSet(WorkGiver_DoBill.relevantThings, bill,
                chosen, rootCell, false);
            WorkGiver_DoBill.relevantThings.Clear();
            return false;
        }
    }

    public class HaulToBuildingModSettings : BaseModSettings
    {
        [Default(false)] public bool DoDes;
        [Default(false)] public bool UseWindows;
        [Unsaved] public WindowPos Window = new();

        public override void ExposeData()
        {
            Scribe_Values.Look(ref UseWindows, "UseWindows");
            Scribe_Values.Look(ref DoDes, "DoDes");
            Window.ExposeData();
        }

        public class WindowPos : IExposable, ICustomSettingsDraw
        {
            public Vector2 Pos;

            public void Render(Listing_Standard listing, string label, string tooltip)
            {
                if (listing.ButtonText("HaulToBuidling.Reset".Translate())) Pos = new Vector2(0, 0);
            }

            public float Height => 30f;

            public void ExposeData()
            {
                Scribe_Values.Look(ref Pos, "windowPos");
            }
        }
    }

    [DefOf]
    public class HaulToBuildingDefOf
    {
        public static BillStoreModeDef StorageBuilding;
        public static BillStoreModeDef Nearest;
    }

    public class ToggleOption
    {
        public bool Enabled;
        public string Label;
        public Action Off;
        public Action On;
        public bool State;

        public ToggleOption(string label, bool state, Action on, Action off, bool enabled = true)
        {
            Label = label;
            State = state;
            On = on;
            Off = off;
            Enabled = enabled;
        }

        public void Toggle()
        {
            if (State) Off();
            else On();
            State = !State;
        }

        public static Func<ToggleOption, FloatMenuOption> ToFloatMenuOption(bool single)
        {
            return o =>
            {
                if (single) return new FloatMenuOption(o.Label, o.Enabled ? o.On : null);
                return new FloatMenuOption(o.Label + " - " + (o.State ? "Off" : "On").Translate(),
                    o.Enabled ? o.Toggle : null);
            };
        }

        public static implicit operator FloatMenuOption(ToggleOption o) => ToFloatMenuOption(false)(o);
    }

    [StaticConstructorOnStartup]
    public class TexStorage
    {
        public static Texture2D SelectStorage = ContentFinder<Texture2D>.Get("UI/SelectStorage");
        public static Texture2D StorageSelection = ContentFinder<Texture2D>.Get("UI/StorageSelection");
    }
}