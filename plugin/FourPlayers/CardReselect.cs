using FITS2.Network;
using KITS.Framework;
using KITS.Level;
using UnityEngine;

namespace FourPlayers;

/// Game bug (also in 3-player games): every client handles every player's ability start in
/// AbilityActionSystemNetSync.StartAbility_Network, which calls LevelPlayerSystem.CardUsed (or ClearCardSelection)
/// and that always ends with SetReadySelectedCards(-1, -1) -- clearing the LOCAL player's selected card even when
/// someone else played. So a card you'd picked but not played yet gets deselected whenever a teammate acts.
///
/// Fix without hooks: watch the local selection each frame. When it is cleared at the moment an ability starts and the
/// card is still in your hand (so you didn't play it), select it again once that ability has finished. Deselecting it
/// yourself (no ability starting) or playing it (it leaves your hand) is left alone.
internal static class CardReselect
{
    private static int _lastSelected = -1;
    private static int _pending = -1;
    private static float _pendingSince;

    internal static void Tick()
    {
        if (!Plugin.KeepCardSelection.Value) return;
        var lps = PersistentSystem<LevelPlayerSystem>.Instance;
        if (lps == null) { _lastSelected = _pending = -1; return; }
        int sel = lps.SelectedCardId;

        if (_lastSelected != -1 && sel == -1)
        {
            var abilities = PersistentSystem<KITS.Abilities.AbilityActionSystem>.Instance;
            if (abilities != null && abilities.IsAnAbilityActive && InLocalHand(_lastSelected))
            {
                _pending = _lastSelected;
                _pendingSince = Time.unscaledTime;
                Plugin.Log.LogInfo($"CardReselect: card {_pending} was deselected by another player's action; will reselect");
            }
        }
        _lastSelected = sel;

        if (_pending == -1) return;
        if (sel != -1 || Time.unscaledTime - _pendingSince > 15f) { _pending = -1; return; }   // picked something else / gave up
        var aas = PersistentSystem<KITS.Abilities.AbilityActionSystem>.Instance;
        var turns = PersistentSystem<LevelTurnSystem>.Instance;
        if (aas == null || aas.IsAnAbilityActive || turns == null || !turns.IsPlayerPhase()) return;
        int card = _pending;
        _pending = -1;
        if (!InLocalHand(card)) return;
        Select(lps, card);
        Plugin.Log.LogInfo($"CardReselect: reselected card {card} (now {lps.SelectedCardId})");
    }

    internal static bool InLocalHand(int cardId)
    {
        var me = PersistentSystem<NetworkManagerSystem>.Instance?.LocalPlayerInstance;
        var res = PersistentSystem<FITS2.Level.LevelPlayerResourceSystem>.Instance;
        if (me == null || res == null) return false;
        var hand = res.GetCardPile(me._clientId, ECardPile.Hand);
        if (hand == null) return false;
        for (int i = 0; i < hand.Count; i++) if (hand[i].Id == cardId) return true;
        return false;
    }

    /// Select the local character (if nothing is selected) and then the card -- what clicking the card does.
    internal static void Select(LevelPlayerSystem lps, int cardId)
    {
        if (lps.SelectedEntity == Il2CppSystem.Guid.Empty)
        {
            var me = PersistentSystem<NetworkManagerSystem>.Instance?.LocalPlayerInstance;
            var ent = me == null ? null : PersistentSystem<KITS.Entities.EntitySystem>.Instance?.GetFirstEntityClientHasAuthorityOver(me._clientId);
            if (ent.HasValue) lps.SelectEntity(ent.Value, false);
        }
        lps.SelectEntityAbilityFromID(cardId);
    }
}
