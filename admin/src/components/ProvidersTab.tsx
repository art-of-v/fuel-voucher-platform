import { useState } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Plus, Trash2, Edit2, Save, X, Loader2, ChevronDown, ChevronRight, History
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { apiRequest } from "@/lib/api-client";
import { toast } from "sonner";
import { formatDate } from "@/lib/utils";

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
  const [newFuelFinalPrice, setNewFuelFinalPrice] = useState("");
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
      setNewFuelFinalPrice("");
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
        supplierPricePerLiter: fuel.supplierPricePerLiter,
        marginUahPerLiter: fuel.marginUahPerLiter,
        finalPricePerLiter: fuel.finalPricePerLiter,
        marginPercent: fuel.marginPercent ?? undefined,
      }
    });
  };

  const saveFuel = (fuel: ProviderFuelDto) => {
    const vals = editValues[fuel.id];
    if (!vals) return;
    updateFuelMutation.mutate({
      fuelId: fuel.id,
      data: {
        ...fuel,
        supplierPricePerLiter: vals.supplierPricePerLiter ?? fuel.supplierPricePerLiter,
        marginUahPerLiter: vals.marginUahPerLiter ?? fuel.marginUahPerLiter,
        finalPricePerLiter: vals.finalPricePerLiter ?? fuel.finalPricePerLiter,
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
        finalPricePerLiter: parseFloat(newFuelFinalPrice) || 0,
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
      {providers.map((provider) => {
        const isExpanded = expandedProvider === provider.id;
        const isHistoryExpanded = expandedHistory === provider.id;

        return (
          <div key={provider.id} className="bg-card border border-border rounded-xl overflow-hidden">
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
                    {provider.fuels.length} {t('nav.fueltypes')} · {provider.nominals.length} nominals
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <Button
                  variant="ghost" size="sm"
                  onClick={(e) => { e.stopPropagation(); deleteProviderMutation.mutate(provider.id); }}
                  className="text-destructive hover:text-destructive"
                >
                  <Trash2 className="w-4 h-4" />
                </Button>
                {isExpanded ? <ChevronDown className="w-5 h-5 text-muted-foreground" /> : <ChevronRight className="w-5 h-5 text-muted-foreground" />}
              </div>
            </div>

            {/* Expanded Content */}
            {isExpanded && (
              <div className="border-t border-border p-4 space-y-6">
                {/* Fuels Table */}
                <div>
                  <div className="flex items-center justify-between mb-3">
                    <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">{t('nav.fuelprices')}</h4>
                    <Button variant="outline" size="sm" onClick={() => setAddingFuel(provider.id)}>
                      <Plus className="w-3.5 h-3.5 mr-1" /> {t('common.add')}
                    </Button>
                  </div>

                  <div className="overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-muted">
                        <tr>
                          <th className="text-left p-3">{t('table.name')}</th>
                          <th className="text-right p-3">{t('price.supplier')}</th>
                          <th className="text-right p-3">{t('price.margin')}</th>
                          <th className="text-right p-3">{t('price.final')}</th>
                          <th className="text-center p-3">{t('table.nominals')}</th>
                          <th className="text-center p-3">{t('common.actions')}</th>
                        </tr>
                      </thead>
                      <tbody>
                        {provider.fuels.map((fuel) => {
                          const isEditing = editingFuel === fuel.id;
                          const vals = editValues[fuel.id];

                          return (
                            <tr key={fuel.id} className="border-t border-border">
                              <td className="p-3 font-medium">{fuel.name}</td>
                              <td className="p-3">
                                {isEditing ? (
                                  <Input
                                    type="number" step="0.01"
                                    value={vals?.supplierPricePerLiter ?? ""}
                                    onChange={(e) => setEditValues(prev => ({
                                      ...prev, [fuel.id]: { ...prev[fuel.id], supplierPricePerLiter: parseFloat(e.target.value) || 0 }
                                    }))}
                                    className="w-24 h-8 text-right"
                                  />
                                ) : (
                                  <span className="block text-right">{fuel.supplierPricePerLiter.toFixed(2)}</span>
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
                                    className="w-24 h-8 text-right"
                                  />
                                ) : (
                                  <span className="block text-right text-primary">{fuel.marginUahPerLiter.toFixed(2)}</span>
                                )}
                              </td>
                              <td className="p-3">
                                {isEditing ? (
                                  <Input
                                    type="number" step="0.01"
                                    value={vals?.finalPricePerLiter ?? ""}
                                    onChange={(e) => setEditValues(prev => ({
                                      ...prev, [fuel.id]: { ...prev[fuel.id], finalPricePerLiter: parseFloat(e.target.value) || 0 }
                                    }))}
                                    className="w-24 h-8 text-right"
                                  />
                                ) : (
                                  <span className="block text-right font-bold">{fuel.finalPricePerLiter.toFixed(2)}</span>
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
                                      <Button variant="ghost" size="sm" onClick={() => saveFuel(fuel)} className="text-green-400">
                                        <Save className="w-3.5 h-3.5" />
                                      </Button>
                                      <Button variant="ghost" size="sm" onClick={() => setEditingFuel(null)}>
                                        <X className="w-3.5 h-3.5" />
                                      </Button>
                                    </>
                                  ) : (
                                    <>
                                      <Button variant="ghost" size="sm" onClick={() => startEditFuel(fuel)} className="text-blue-400">
                                        <Edit2 className="w-3.5 h-3.5" />
                                      </Button>
                                      <Button variant="ghost" size="sm" onClick={() => deleteFuelMutation.mutate(fuel.id)} className="text-destructive">
                                        <Trash2 className="w-3.5 h-3.5" />
                                      </Button>
                                    </>
                                  )}
                                </div>
                              </td>
                            </tr>
                          );
                        })}
                        {/* Add Fuel Form */}
                        {addingFuel === provider.id && (
                          <tr className="border-t border-border bg-muted/30">
                            <td className="p-2">
                              <Input
                                placeholder={t('forms.fuelNamePlaceholder')}
                                value={newFuelName}
                                onChange={(e) => setNewFuelName(e.target.value)}
                                className="h-8"
                              />
                            </td>
                            <td className="p-2">
                              <Input
                                type="number" step="0.01" placeholder="0.00"
                                value={newFuelSupplierPrice}
                                onChange={(e) => setNewFuelSupplierPrice(e.target.value)}
                                className="h-8 w-24 text-right"
                              />
                            </td>
                            <td className="p-2">
                              <Input
                                type="number" step="0.01" placeholder="0.00"
                                value={newFuelMargin}
                                onChange={(e) => setNewFuelMargin(e.target.value)}
                                className="h-8 w-24 text-right"
                              />
                            </td>
                            <td className="p-2">
                              <Input
                                type="number" step="0.01" placeholder="0.00"
                                value={newFuelFinalPrice}
                                onChange={(e) => setNewFuelFinalPrice(e.target.value)}
                                className="h-8 w-24 text-right"
                              />
                            </td>
                            <td className="p-2 text-center text-xs text-muted-foreground">auto</td>
                            <td className="p-2">
                              <div className="flex justify-center gap-1">
                                <Button variant="ghost" size="sm" onClick={() => handleAddFuel(provider.id)} className="text-green-400">
                                  <Save className="w-3.5 h-3.5" />
                                </Button>
                                <Button variant="ghost" size="sm" onClick={() => setAddingFuel(null)}>
                                  <X className="w-3.5 h-3.5" />
                                </Button>
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
                    <Button variant="outline" size="sm" onClick={() => {
                      setEditingNominals(provider.id);
                      setNominalInput(provider.nominals.join(", "));
                    }}>
                      <Edit2 className="w-3.5 h-3.5 mr-1" /> {t('common.edit')}
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
                      <Button size="sm" onClick={() => handleSaveNominals(provider)}>
                        <Save className="w-4 h-4 mr-1" /> {t('common.save')}
                      </Button>
                      <Button variant="ghost" size="sm" onClick={() => setEditingNominals(null)}>
                        <X className="w-4 h-4" />
                      </Button>
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-2">
                      {provider.nominals.map((n) => (
                        <span key={n} className="px-3 py-1 bg-muted rounded-full text-sm font-mono">
                          {n} L
                        </span>
                      ))}
                    </div>
                  )}
                </div>

                {/* History Section */}
                <div>
                  <div
                    className="flex items-center gap-2 cursor-pointer text-muted-foreground hover:text-foreground transition-colors"
                    onClick={() => setExpandedHistory(isHistoryExpanded ? null : provider.id)}
                  >
                    <History className="w-4 h-4" />
                    <span className="text-sm font-medium">{t('common.history')}</span>
                    {isHistoryExpanded ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
                  </div>
                  {isHistoryExpanded && (
                    <div className="mt-2 max-h-64 overflow-y-auto space-y-1">
                      {history.length === 0 ? (
                        <p className="text-sm text-muted-foreground p-2">{t('common.noData')}</p>
                      ) : (
                        history.map((evt) => (
                          <div key={evt.id} className="flex items-start gap-2 p-2 rounded bg-muted/30 text-sm">
                            <div className={`w-1.5 h-1.5 rounded-full mt-1.5 shrink-0 ${
                              evt.eventType === 'ProviderCreated' ? 'bg-green-500' :
                              evt.eventType === 'ProviderDeleted' ? 'bg-red-500' :
                              evt.eventType === 'PriceChanged' ? 'bg-yellow-500' :
                              'bg-blue-500'
                            }`} />
                            <div className="flex-1 min-w-0">
                              <p className="text-xs font-medium">{evt.summary}</p>
                              <p className="text-xs text-muted-foreground">
                                {evt.changedByUserName ?? 'System'} · {formatDate(evt.changedAtUtc)}
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
    </div>
  );
}
