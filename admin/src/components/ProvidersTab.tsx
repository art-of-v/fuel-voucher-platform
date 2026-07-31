import { useState } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Plus, Trash2, Edit2, Save, X, Loader2, ChevronDown, ChevronRight, History, AlertTriangle
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { apiRequest } from "@/lib/api-client";
import { toast } from "sonner";
import { formatDateTime } from "@/lib/utils";

interface ProviderFuelDto {
  id: string;
  name: string;
  supplierPricePerLiter: number;
  marginUahPerLiter: number;
  marginPercent: number | null;
  finalPricePerLiter: number;
  packageLiters: number[];
}

interface ProviderDto {
  id: string;
  name: string;
  logoText: string;
  color: string;
  fuels: ProviderFuelDto[];
  nominals: number[];
}

interface ProviderEventDto {
  id: string;
  aggregateType: string;
  aggregateId: string;
  eventType: string;
  oldValue: string | null;
  newValue: string;
  changedByUserName: string | null;
  summary: string;
  changedAtUtc: string;
}

const DEFAULT_PROVIDER_COLOR = "#00ff80";

export default function ProvidersTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [expandedProvider, setExpandedProvider] = useState<string | null>(null);
  const [expandedHistory, setExpandedHistory] = useState<string | null>(null);
  const [editingFuel, setEditingFuel] = useState<string | null>(null);
  const [editValues, setEditValues] = useState<Record<string, Partial<ProviderFuelDto>>>({});
  const [addingFuel, setAddingFuel] = useState<string | null>(null);
  const [newFuelName, setNewFuelName] = useState("");
  const [newFuelSupplierPrice, setNewFuelSupplierPrice] = useState("");
  const [newFuelMargin, setNewFuelMargin] = useState("");
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);

  const [isAddingProvider, setIsAddingProvider] = useState(false);
  const [newProvider, setNewProvider] = useState({ name: "", logoText: "", color: DEFAULT_PROVIDER_COLOR });
  const [editingProviderId, setEditingProviderId] = useState<string | null>(null);
  const [editProvider, setEditProvider] = useState({ name: "", logoText: "", color: DEFAULT_PROVIDER_COLOR });
  const [confirmDeleteFuel, setConfirmDeleteFuel] = useState<string | null>(null);

  const newFuelFinalPrice = (parseFloat(newFuelSupplierPrice) || 0) + (parseFloat(newFuelMargin) || 0);

  const [editingNominals, setEditingNominals] = useState<string | null>(null);
  const [nominalInput, setNominalInput] = useState("");

  const { data: providers = [], isLoading } = useQuery<ProviderDto[]>({
    queryKey: ["/api/admin/providers"],
  });

  const { data: history = [] } = useQuery<ProviderEventDto[]>({
    queryKey: ["/api/admin/providers", expandedHistory, "history"],
    enabled: !!expandedHistory,
    queryFn: async () => {
      if (!expandedHistory) return [];
      const res = await apiRequest<any, ProviderEventDto[]>("GET", `/api/admin/providers/${expandedHistory}/history`);
      return res;
    }
  });

  const createProviderMutation = useMutation({
    mutationFn: async (data: { id: string; name: string; logoText: string; color: string }) => {
      await apiRequest("POST", "/api/admin/providers", data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/providers"] });
      setIsAddingProvider(false);
      setNewProvider({ name: "", logoText: "", color: DEFAULT_PROVIDER_COLOR });
      toast.success(t('common.created'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const handleCreateProvider = () => {
    const name = newProvider.name.trim();
    if (!name) return;
    const id = name.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 32) || crypto.randomUUID();
    createProviderMutation.mutate({ id, name, logoText: newProvider.logoText.trim(), color: newProvider.color });
  };

  const updateProviderMutation = useMutation({
    mutationFn: async ({ id, data }: { id: string; data: { id: string; name: string; logoText: string; color: string } }) => {
      await apiRequest("PUT", `/api/admin/providers/${id}`, data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/providers"] });
      setEditingProviderId(null);
      toast.success(t('common.saved'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const startEditProvider = (provider: ProviderDto) => {
    setEditingProviderId(provider.id);
    setEditProvider({ name: provider.name, logoText: provider.logoText, color: provider.color });
  };

  const handleSaveProvider = () => {
    const id = editingProviderId;
    if (!id || !editProvider.name.trim()) return;
    updateProviderMutation.mutate({ id, data: { id, name: editProvider.name.trim(), logoText: editProvider.logoText.trim(), color: editProvider.color } });
  };

  const deleteProviderMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest("DELETE", `/api/admin/providers/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/providers"] });
      toast.success(t('common.deleted'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const updateFuelMutation = useMutation({
    mutationFn: async ({ fuelId, data }: { fuelId: string; data: ProviderFuelDto }) => {
      await apiRequest("PUT", `/api/admin/providers/fuels/${fuelId}`, data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/providers"] });
      setEditingFuel(null);
      toast.success(t('common.saved'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const deleteFuelMutation = useMutation({
    mutationFn: async (fuelId: string) => {
      await apiRequest("DELETE", `/api/admin/providers/fuels/${fuelId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/providers"] });
      toast.success(t('common.deleted'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const addFuelMutation = useMutation({
    mutationFn: async ({ providerId, data }: { providerId: string; data: Partial<ProviderFuelDto> }) => {
      await apiRequest("POST", `/api/admin/providers/${providerId}/fuels`, data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/providers"] });
      setAddingFuel(null);
      setNewFuelName("");
      setNewFuelSupplierPrice("");
      setNewFuelMargin("");
      toast.success(t('common.created'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const updateNominalsMutation = useMutation({
    mutationFn: async ({ providerId, nominals }: { providerId: string; nominals: number[] }) => {
      await apiRequest("PUT", `/api/admin/providers/${providerId}/nominals`, nominals);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/providers"] });
      setEditingNominals(null);
      toast.success(t('common.saved'));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const startEditFuel = (fuel: ProviderFuelDto) => {
    setEditingFuel(fuel.id);
    setEditValues({
      [fuel.id]: {
        name: fuel.name,
        supplierPricePerLiter: fuel.supplierPricePerLiter,
        marginUahPerLiter: fuel.marginUahPerLiter,
        marginPercent: fuel.marginPercent ?? undefined,
      }
    });
  };

  const saveFuel = (fuel: ProviderFuelDto) => {
    const vals = editValues[fuel.id];
    if (!vals) return;
    const supplier = vals.supplierPricePerLiter ?? fuel.supplierPricePerLiter;
    const margin = vals.marginUahPerLiter ?? fuel.marginUahPerLiter;
    const finalPrice = supplier + margin;
    updateFuelMutation.mutate({
      fuelId: fuel.id,
      data: {
        ...fuel,
        name: vals.name?.trim() || fuel.name,
        supplierPricePerLiter: supplier,
        marginUahPerLiter: margin,
        finalPricePerLiter: finalPrice,
        marginPercent: vals.marginPercent ?? fuel.marginPercent,
      }
    });
  };

  const handleAddFuel = (providerId: string) => {
    if (!newFuelName || !newFuelFinalPrice) return;
    addFuelMutation.mutate({
      providerId,
      data: {
        name: newFuelName,
        supplierPricePerLiter: parseFloat(newFuelSupplierPrice) || 0,
        marginUahPerLiter: parseFloat(newFuelMargin) || 0,
        finalPricePerLiter: newFuelFinalPrice,
        packageLiters: [],
      }
    });
  };

  const handleSaveNominals = (provider: ProviderDto) => {
    const nominals = nominalInput.split(",").map(s => parseInt(s.trim())).filter(n => !isNaN(n) && n > 0);
    if (nominals.length === 0) return;
    updateNominalsMutation.mutate({ providerId: provider.id, nominals });
  };

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-8">
        <Loader2 className="w-5 h-5 animate-spin" />
        {t('common.loading')}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-end">
        <Button variant="outline" size="sm" onClick={() => setIsAddingProvider(true)}>
          <Plus className="w-3.5 h-3.5 mr-1" />
          {t('providers.addProvider')}
        </Button>
      </div>

      {isAddingProvider && (
        <div className="bg-card border border-border rounded-xl p-4 animate-in fade-in slide-in-from-top-2 duration-200">
          <div className="flex items-end gap-3 flex-wrap">
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('table.name')}</label>
              <Input
                autoFocus
                placeholder={t('providers.namePlaceholder')}
                value={newProvider.name}
                onChange={(e) => setNewProvider(prev => ({ ...prev, name: e.target.value }))}
                className="h-8 w-64"
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('providers.logoText')}</label>
              <Input
                maxLength={3}
                placeholder={t('providers.logoTextPlaceholder')}
                value={newProvider.logoText}
                onChange={(e) => setNewProvider(prev => ({ ...prev, logoText: e.target.value }))}
                className="h-8 w-28"
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('providers.color')}</label>
              <div className="flex items-center gap-2">
                <input
                  type="color"
                  value={newProvider.color}
                  onChange={(e) => setNewProvider(prev => ({ ...prev, color: e.target.value }))}
                  className="h-8 w-12 cursor-pointer rounded bg-transparent border border-border"
                />
                <span className="text-xs font-mono text-muted-foreground">{newProvider.color}</span>
              </div>
            </div>
            <div className="flex items-end gap-1 pb-0.5 ml-auto">
              <Button size="sm" onClick={handleCreateProvider} disabled={!newProvider.name.trim() || createProviderMutation.isPending} className="h-8">
                {createProviderMutation.isPending ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <Save className="w-3.5 h-3.5 mr-1" />}
                {t('common.create')}
              </Button>
              <Button variant="ghost" size="sm" onClick={() => { setIsAddingProvider(false); setNewProvider({ name: "", logoText: "", color: DEFAULT_PROVIDER_COLOR }); }} disabled={createProviderMutation.isPending} className="h-8">
                <X className="w-3.5 h-3.5" />
              </Button>
            </div>
          </div>
        </div>
      )}

      {editingProviderId && (() => {
        const provider = providers.find(p => p.id === editingProviderId);
        if (!provider) return null;
        return (
          <div className="bg-card border border-border rounded-xl p-4 animate-in fade-in slide-in-from-top-2 duration-200">
            <div className="flex items-center justify-between mb-3">
              <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">{t('providers.editProvider')}</h4>
            </div>
            <div className="flex items-end gap-3 flex-wrap">
              <div className="flex flex-col gap-1">
                <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('table.name')}</label>
                <Input
                  autoFocus
                  placeholder={t('providers.namePlaceholder')}
                  value={editProvider.name}
                  onChange={(e) => setEditProvider(prev => ({ ...prev, name: e.target.value }))}
                  className="h-8 w-64"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('providers.logoText')}</label>
                <Input
                  maxLength={3}
                  placeholder={t('providers.logoTextPlaceholder')}
                  value={editProvider.logoText}
                  onChange={(e) => setEditProvider(prev => ({ ...prev, logoText: e.target.value }))}
                  className="h-8 w-28"
                />
              </div>
              <div className="flex flex-col gap-1">
                <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('providers.color')}</label>
                <div className="flex items-center gap-2">
                  <input
                    type="color"
                    value={editProvider.color}
                    onChange={(e) => setEditProvider(prev => ({ ...prev, color: e.target.value }))}
                    className="h-8 w-12 cursor-pointer rounded bg-transparent border border-border"
                  />
                  <span className="text-xs font-mono text-muted-foreground">{editProvider.color}</span>
                </div>
              </div>
              <div className="flex items-end gap-1 pb-0.5 ml-auto">
                <Button size="sm" onClick={handleSaveProvider} disabled={!editProvider.name.trim() || updateProviderMutation.isPending} className="h-8">
                  {updateProviderMutation.isPending ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <Save className="w-3.5 h-3.5 mr-1" />}
                  {t('common.save')}
                </Button>
                <Button variant="ghost" size="sm" onClick={() => setEditingProviderId(null)} disabled={updateProviderMutation.isPending} className="h-8">
                  <X className="w-3.5 h-3.5" />
                </Button>
              </div>
            </div>
          </div>
        );
      })()}

      {providers.map((provider) => {
        const isExpanded = expandedProvider === provider.id;
        const isHistoryExpanded = expandedHistory === provider.id;
        const busy = updateFuelMutation.isPending || deleteFuelMutation.isPending || deleteProviderMutation.isPending;

        return (
          <div key={provider.id} className="bg-card border border-border rounded-xl overflow-hidden transition-all duration-200 hover:border-primary/20">
            {/* Provider Header */}
            <div
              className="flex items-center justify-between p-4 cursor-pointer hover:bg-muted/50 transition-colors"
              onClick={() => setExpandedProvider(isExpanded ? null : provider.id)}
            >
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-lg flex items-center justify-center text-lg font-bold text-black"
                     style={{ backgroundColor: provider.color }}>
                  {provider.logoText}
                </div>
                <div>
                  <h3 className="text-lg font-bold">{provider.name}</h3>
                  <p className="text-xs text-muted-foreground">
                    {provider.fuels.length} {t('nav.fueltypes')} · {t('providers.nominalsCount', provider.nominals.length.toString())}
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <Button
                  variant="ghost" size="sm"
                  onClick={(e) => { e.stopPropagation(); startEditProvider(provider); }}
                  className="text-blue-400 hover:text-blue-300"
                >
                  <Edit2 className="w-4 h-4" />
                </Button>
                {confirmDelete === provider.id ? (
                  <div className="flex items-center gap-1 animate-in fade-in slide-in-from-right-2">
                    <span className="text-xs text-destructive font-medium flex items-center gap-1 mr-1">
                      <AlertTriangle className="w-3 h-3" /> {t('providers.deleteConfirm', provider.name)}
                    </span>
                    <Button
                      variant="destructive" size="sm" className="h-7 text-xs"
                      disabled={deleteProviderMutation.isPending}
                      onClick={(e) => {
                        e.stopPropagation();
                        deleteProviderMutation.mutate(provider.id);
                        setConfirmDelete(null);
                      }}
                    >
                      {deleteProviderMutation.isPending ? <Loader2 className="w-3 h-3 animate-spin" /> : <Trash2 className="w-3 h-3" />}
                    </Button>
                    <Button variant="ghost" size="sm" className="h-7 text-xs" onClick={(e) => { e.stopPropagation(); setConfirmDelete(null); }}>
                      <X className="w-3 h-3" />
                    </Button>
                  </div>
                ) : (
                  <Button
                    variant="ghost" size="sm"
                    onClick={(e) => { e.stopPropagation(); setConfirmDelete(provider.id); }}
                    className="text-destructive hover:text-destructive"
                  >
                    {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : <Trash2 className="w-4 h-4" />}
                  </Button>
                )}
                {isExpanded ? <ChevronDown className="w-5 h-5 text-muted-foreground" /> : <ChevronRight className="w-5 h-5 text-muted-foreground" />}
              </div>
            </div>

            {/* Expanded Content */}
            {isExpanded && (
              <div className="border-t border-border p-4 md:p-6 space-y-6 animate-in fade-in slide-in-from-top-2 duration-200">
                {/* Fuels Table */}
                <div>
                  <div className="flex items-center justify-between mb-3">
                    <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">
                      {t('price.title')}
                    </h4>
                    <Button variant="outline" size="sm" onClick={() => setAddingFuel(provider.id)} disabled={addFuelMutation.isPending}>
                      {addFuelMutation.isPending ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <Plus className="w-3.5 h-3.5 mr-1" />}
                      {t('common.add')}
                    </Button>
                  </div>

                  <div className="overflow-x-auto rounded-lg border border-border">
                    <table className="w-full text-sm">
                      <thead className="bg-muted/50">
                        <tr>
                          <th className="text-left p-3 whitespace-nowrap">{t('table.name')}</th>
                          <th className="text-right p-3 whitespace-nowrap">
                            <div>{t('price.supplier')}</div>
                            <div className="text-[10px] text-muted-foreground font-normal">{t('price.unit')}</div>
                          </th>
                          <th className="text-right p-3 whitespace-nowrap">
                            <div>{t('price.margin')}</div>
                            <div className="text-[10px] text-muted-foreground font-normal">{t('price.unit')}</div>
                          </th>
                          <th className="text-right p-3 whitespace-nowrap">
                            <div>{t('price.final')}</div>
                            <div className="text-[10px] text-muted-foreground font-normal">{t('price.unit')}</div>
                          </th>
                          <th className="text-center p-3 whitespace-nowrap">{t('table.nominals')}</th>
                          <th className="text-center p-3 whitespace-nowrap">{t('common.actions')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {provider.fuels.map((fuel) => {
                          const isEditing = editingFuel === fuel.id;
                          const vals = editValues[fuel.id];
                          const saving = updateFuelMutation.isPending && editingFuel === fuel.id;
                          const deleting = deleteFuelMutation.isPending;
                          const computedFinal = (vals?.supplierPricePerLiter ?? fuel.supplierPricePerLiter) + (vals?.marginUahPerLiter ?? fuel.marginUahPerLiter);

                          return (
                            <tr key={fuel.id} className="border-t border-border hover:bg-muted/20 transition-colors">
                              <td className="p-3">
                                {isEditing ? (
                                  <Input
                                    value={vals?.name ?? ""}
                                    onChange={(e) => setEditValues(prev => ({
                                      ...prev, [fuel.id]: { ...prev[fuel.id], name: e.target.value }
                                    }))}
                                    className="w-32 h-8 text-xs"
                                  />
                                ) : (
                                  <span className="font-medium">{fuel.name}</span>
                                )}
                              </td>
                              <td className="p-3">
                                {isEditing ? (
                                  <Input
                                    type="number" step="0.01"
                                    value={vals?.supplierPricePerLiter ?? ""}
                                    onChange={(e) => setEditValues(prev => ({
                                      ...prev, [fuel.id]: { ...prev[fuel.id], supplierPricePerLiter: parseFloat(e.target.value) || 0 }
                                    }))}
                                    className="w-24 h-8 text-right text-xs"
                                  />
                                ) : (
                                  <span className="block text-right tabular-nums">{fuel.supplierPricePerLiter.toFixed(2)}</span>
                                )}
                              </td>
                              <td className="p-3">
                                {isEditing ? (
                                  <Input
                                    type="number" step="0.01"
                                    value={vals?.marginUahPerLiter ?? ""}
                                    onChange={(e) => setEditValues(prev => ({
                                      ...prev, [fuel.id]: { ...prev[fuel.id], marginUahPerLiter: parseFloat(e.target.value) || 0 }
                                    }))}
                                    className="w-24 h-8 text-right text-xs"
                                  />
                                ) : (
                                  <span className="block text-right tabular-nums text-primary">{fuel.marginUahPerLiter.toFixed(2)}</span>
                                )}
                              </td>
                              <td className="p-3">
                                {isEditing ? (
                                  <span className="block text-right font-bold text-primary tabular-nums px-2 py-1.5 bg-primary/5 rounded text-sm">
                                    {computedFinal.toFixed(2)}
                                  </span>
                                ) : (
                                  <span className="block text-right font-bold tabular-nums">{fuel.finalPricePerLiter.toFixed(2)}</span>
                                )}
                              </td>
                              <td className="p-3 text-center">
                                <span className="text-xs text-muted-foreground">
                                  {fuel.packageLiters.join(", ")} L
                                </span>
                              </td>
                              <td className="p-3">
                                <div className="flex justify-center gap-1">
                                  {isEditing ? (
                                    <>
                                      <Button size="sm" onClick={() => saveFuel(fuel)} disabled={saving} className="h-8">
                                        {saving ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <Save className="w-3.5 h-3.5 mr-1" />}
                                        {t('common.save')}
                                      </Button>
                                      <Button variant="ghost" size="sm" onClick={() => setEditingFuel(null)} disabled={saving} className="h-8">
                                        <X className="w-3.5 h-3.5" />
                                      </Button>
                                    </>
                                  ) : confirmDeleteFuel === fuel.id ? (
                                    <div className="flex items-center gap-1 animate-in fade-in slide-in-from-right-2">
                                      <Button size="sm" variant="destructive" disabled={deleting} className="h-8"
                                        onClick={() => {
                                          deleteFuelMutation.mutate(fuel.id);
                                          setConfirmDeleteFuel(null);
                                        }}
                                      >
                                        {deleting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Trash2 className="w-3.5 h-3.5 mr-1" />}
                                        {t('providers.deleteConfirm', fuel.name)}
                                      </Button>
                                      <Button variant="ghost" size="sm" onClick={() => setConfirmDeleteFuel(null)} disabled={deleting} className="h-8">
                                        <X className="w-3.5 h-3.5" />
                                      </Button>
                                    </div>
                                  ) : (
                                    <>
                                      <Button variant="ghost" size="sm" onClick={() => startEditFuel(fuel)} className="text-blue-400 hover:text-blue-300 h-8">
                                        <Edit2 className="w-3.5 h-3.5" />
                                      </Button>
                                      <Button variant="ghost" size="sm" disabled={deleting}
                                        onClick={() => setConfirmDeleteFuel(fuel.id)}
                                        className="text-destructive hover:text-destructive h-8"
                                      >
                                        {deleting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Trash2 className="w-3.5 h-3.5" />}
                                      </Button>
                                    </>
                                  )}
                                </div>
                              </td>
                            </tr>
                          );
                        })}
                        {/* Add Fuel Row */}
                        {addingFuel === provider.id && (
                          <tr className="border-t border-border bg-muted/30">
                            <td colSpan={6} className="p-3">
                              <div className="flex items-end gap-3 flex-wrap">
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('table.name')}</label>
                                  <Input
                                    placeholder="A-95"
                                    value={newFuelName}
                                    onChange={(e) => setNewFuelName(e.target.value)}
                                    className="h-8 w-36"
                                  />
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('price.supplier')}, {t('price.unit')}</label>
                                  <Input
                                    type="number" step="0.01" placeholder="50.00"
                                    value={newFuelSupplierPrice}
                                    onChange={(e) => setNewFuelSupplierPrice(e.target.value)}
                                    className="h-8 w-28 text-right"
                                  />
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('price.margin')}, {t('price.unit')}</label>
                                  <Input
                                    type="number" step="0.01" placeholder="2.00"
                                    value={newFuelMargin}
                                    onChange={(e) => setNewFuelMargin(e.target.value)}
                                    className="h-8 w-28 text-right"
                                  />
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('price.final')}, {t('price.unit')}</label>
                                  <div className="h-8 flex items-center text-right font-bold text-primary tabular-nums text-sm bg-primary/5 rounded px-3">
                                    {newFuelFinalPrice.toFixed(2)}
                                  </div>
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t('table.nominals')}</label>
                                  <div className="h-8 flex items-center text-xs text-muted-foreground">{t('price.nominalsAuto')}</div>
                                </div>
                                <div className="flex items-end gap-1 pb-0.5">
                                  <Button size="sm"
                                    onClick={() => handleAddFuel(provider.id)}
                                    disabled={!newFuelName || !newFuelFinalPrice || addFuelMutation.isPending}
                                    className="h-8"
                                  >
                                    {addFuelMutation.isPending ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <Plus className="w-3.5 h-3.5 mr-1" />}
                                    {t('common.add')}
                                  </Button>
                                  <Button variant="ghost" size="sm" onClick={() => setAddingFuel(null)} disabled={addFuelMutation.isPending} className="h-8">
                                    <X className="w-3.5 h-3.5" />
                                  </Button>
                                </div>
                              </div>
                            </td>
                          </tr>
                        )}
                      </tbody>
                    </table>
                  </div>
                </div>

                {/* Nominals Section */}
                <div>
                  <div className="flex items-center justify-between mb-3">
                    <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">{t('table.nominals')}</h4>
                    <Button variant="outline" size="sm" disabled={updateNominalsMutation.isPending} onClick={() => {
                      setEditingNominals(provider.id);
                      setNominalInput(provider.nominals.join(", "));
                    }}>
                      {updateNominalsMutation.isPending ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <Edit2 className="w-3.5 h-3.5 mr-1" />}
                      {t('common.edit')}
                    </Button>
                  </div>
                  {editingNominals === provider.id ? (
                    <div className="flex gap-2 items-center">
                      <Input
                        value={nominalInput}
                        onChange={(e) => setNominalInput(e.target.value)}
                        placeholder="2, 3, 5, 10, 20, 50, 100"
                        className="flex-1"
                      />
                      <Button size="sm" disabled={updateNominalsMutation.isPending} onClick={() => handleSaveNominals(provider)}>
                        {updateNominalsMutation.isPending ? <Loader2 className="w-4 h-4 mr-1 animate-spin" /> : <Save className="w-4 h-4 mr-1" />}
                        {t('common.save')}
                      </Button>
                      <Button variant="ghost" size="sm" onClick={() => setEditingNominals(null)} disabled={updateNominalsMutation.isPending}>
                        <X className="w-4 h-4" />
                      </Button>
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-2">
                      {provider.nominals.map((n) => (
                        <span key={n} className="px-3 py-1 bg-muted rounded-full text-sm font-mono border border-border">
                          {n} L
                        </span>
                      ))}
                    </div>
                  )}
                </div>

                {/* History Section */}
                <div>
                  <div
                    className="flex items-center gap-2 cursor-pointer text-muted-foreground hover:text-foreground transition-colors p-2 -ml-2 rounded hover:bg-muted/30"
                    onClick={() => setExpandedHistory(isHistoryExpanded ? null : provider.id)}
                  >
                    <History className="w-4 h-4" />
                    <span className="text-sm font-medium">{t('common.history')}</span>
                    {isHistoryExpanded ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
                  </div>
                  {isHistoryExpanded && (
                    <div className="mt-2 max-h-64 overflow-y-auto space-y-1 animate-in fade-in slide-in-from-top-1 duration-150">
                      {history.length === 0 ? (
                        <p className="text-sm text-muted-foreground p-2">{t('common.noData')}</p>
                      ) : (
                        history.map((evt) => (
                          <div key={evt.id} className="flex items-start gap-2 p-2 rounded bg-muted/30 text-sm hover:bg-muted/50 transition-colors">
                            <div className={`w-1.5 h-1.5 rounded-full mt-1.5 shrink-0 ${
                              evt.eventType === 'ProviderCreated' ? 'bg-green-500' :
                              evt.eventType === 'ProviderDeleted' ? 'bg-red-500' :
                              evt.eventType === 'PriceChanged' ? 'bg-yellow-500' :
                              evt.eventType === 'FuelAdded' ? 'bg-blue-500' :
                              evt.eventType === 'FuelRemoved' ? 'bg-orange-500' :
                              'bg-blue-500'
                            }`} />
                            <div className="flex-1 min-w-0">
                              <p className="text-xs font-medium">{evt.summary}</p>
                              <p className="text-xs text-muted-foreground">
                                {evt.changedByUserName ?? 'System'} · {formatDateTime(evt.changedAtUtc)}
                              </p>
                            </div>
                          </div>
                        ))
                      )}
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>
        );
      })}
      {providers.length === 0 && !isLoading && (
        <div className="text-center py-16 text-muted-foreground">
          <h3 className="text-lg font-medium mb-1">{t('common.noData')}</h3>
          <p className="text-sm mb-4">{t('providers.noProviders')}</p>
          <Button variant="outline" size="sm" onClick={() => setIsAddingProvider(true)}>
            <Plus className="w-3.5 h-3.5 mr-1" />
            {t('providers.addProvider')}
          </Button>
        </div>
      )}
    </div>
  );
}
