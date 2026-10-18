using System;
using System.Linq;
using UnityEngine.UI;

public partial class DmPanelUI
{
    private string[] _referenceDraftBaseline, _statBlockDraftBaseline;
    private string _draftCampaignId;
    private string[] ReferenceDraftValues() => _referenceTitle == null ? Array.Empty<string>() : new[]
    {
        _referenceTitle.text, _referenceCategory.text, _referenceTags.text, _referenceBody.text,
        _referenceMasterOnly.ToString(), _referencePinned.ToString()
    };
    private string[] StatBlockDraftValues() => _statBlockInputs == null ? Array.Empty<string>() :
        _statBlockInputs.Select(input => input.text).Concat(new[] { _statBlockVisibleFieldsDraft.ToString() }).ToArray();
    private bool ReferenceDraftDirty => _referenceDraftBaseline != null && !_referenceDraftBaseline.SequenceEqual(ReferenceDraftValues());
    private bool StatBlockDraftDirty => _statBlockDraftBaseline != null && !_statBlockDraftBaseline.SequenceEqual(StatBlockDraftValues());
    public bool HasUnsavedLibraryDrafts => ReferenceDraftDirty || StatBlockDraftDirty;
    private void RememberReferenceDraft() { _referenceDraftBaseline = ReferenceDraftValues(); _draftCampaignId = SceneFileStore.CampaignId; }
    private void RememberStatBlockDraft() { _statBlockDraftBaseline = StatBlockDraftValues(); _draftCampaignId = SceneFileStore.CampaignId; }

    private void GuardLibraryDraft(bool reference, Action action)
    {
        bool dirty = reference ? ReferenceDraftDirty : StatBlockDraftDirty;
        if (!dirty) { action(); return; }
        DiceUI.Instance?.ConfirmAction("Отбросить несохранённые правки?",
            "Текст в редакторе " + (reference ? "справочника" : "статблоков") + " изменён. Вернитесь и сохраните его либо подтвердите переход с потерей этих правок.", action);
    }
    private void RequestNewReference() => GuardLibraryDraft(true, ClearReferenceInputs);
    private void RequestNewStatBlock() => GuardLibraryDraft(false, ClearStatBlockInputs);
    private void RequestCharacterImport() => GuardLibraryDraft(false, ImportCharacterDataToStatBlock);
    private void RequestReferenceImport() => GuardLibraryDraft(true, SceneFileStore.ImportReferenceLibraryDialog);
    private void ReloadSelectedStatBlock() => GuardLibraryDraft(false, () =>
    {
        var block = Array.Find(SceneFileStore.GetStatBlocks(), item => item.id == _selectedStatBlockId);
        if (block == null) ClearStatBlockInputs(); else SelectStatBlockCore(block);
    });
    private void SelectReference(ReferenceEntry entry)
    {
        if (entry == null || entry.id == _selectedReferenceId && !ReferenceDraftDirty) return;
        GuardLibraryDraft(true, () => SelectReferenceCore(entry));
    }
    private void SelectStatBlock(StatBlockDefinition block)
    {
        if (block == null || block.id == _selectedStatBlockId && !StatBlockDraftDirty) return;
        GuardLibraryDraft(false, () => SelectStatBlockCore(block));
    }
    private void WatchLibraryDrafts()
    {
        foreach (var input in _statBlockInputs)
            input.onValueChanged.AddListener(_ => { if (StatBlockDraftDirty) _statBlockHint.text = "Есть несохранённые правки. Сохраните статблок перед выбором другого."; });
        foreach (var input in new[] { _referenceTitle, _referenceCategory, _referenceTags, _referenceBody })
            input.onValueChanged.AddListener(_ => { if (ReferenceDraftDirty) _referenceHint.text = "Есть несохранённые правки. Сохраните запись перед выбором другой."; });
    }
    public void ResetLibraryDraftsAfterLoad()
    {
        ClearReferenceInputs(); ClearStatBlockInputs();
        _referenceSignature = _statBlockSignature = null;
    }
    public void ResetReferenceDraftAfterImport() { ClearReferenceInputs(); _referenceSignature = null; }
    private void RefreshDraftContext()
    {
        string campaign = SceneFileStore.CampaignId;
        if (!string.IsNullOrEmpty(_draftCampaignId) && _draftCampaignId != campaign) ResetLibraryDraftsAfterLoad();
        _draftCampaignId = campaign;
    }
}
