import { useState } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Plus, Trash2, Edit2, Save, X, Loader2, FileUp, MapPin, AlertTriangle,
  ChevronLeft, ChevronRight,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { apiRequest } from "@/lib/api-client";
import { toast } from "sonner";
import {
  StationNode, PagedResult, isValidCoordinate, deriveNodeId,
} from "@/lib/station-nodes";

interface Brand {
  id: string;
  name: string;
  logoText?: string;
  color?: string;
}

interface ImportResult {
  added: number;
  updated: number;
  errors: { line: number; message: string }[];
}

const PAGE_SIZE = 50;

type NodeForm = {
  id: string;
  stationId: string;
  name: string;
  address: string;
  phone: string;
  city: string;
  stationType: string;
  lat: string;
  lng: string;
};

const EMPTY_FORM: NodeForm = {
  id: "", stationId: "", name: "", address: "", phone: "",
  city: "", stationType: "", lat: "", lng: "",
};

function toNodePayload(form: NodeForm): StationNode {
  const lat = form.lat.trim() === "" ? null : parseFloat(form.lat);
  const lng = form.lng.trim() === "" ? null : parseFloat(form.lng);
  return {
    id: form.id.trim(),
    stationId: form.stationId.trim(),
    name: form.name.trim(),
    address: form.address.trim() || null,
    phone: form.phone.trim() || null,
    city: form.city.trim() || null,
    stationType: form.stationType.trim() || null,
    lat,
    lng,
  };
}

function nodeToForm(n: StationNode): NodeForm {
  return {
    id: n.id,
    stationId: n.stationId,
    name: n.name,
    address: n.address ?? "",
    phone: n.phone ?? "",
    city: n.city ?? "",
    stationType: n.stationType ?? "",
    lat: n.lat != null ? String(n.lat) : "",
    lng: n.lng != null ? String(n.lng) : "",
  };
}

export default function StationNodesTab() {
  const { t } = useI18n();
  const queryClient = useQueryClient();

  const [page, setPage] = useState(1);
  const [stationFilter, setStationFilter] = useState<string>("");

  const [isCreating, setIsCreating] = useState(false);
  const [form, setForm] = useState<NodeForm>(EMPTY_FORM);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);

  const [importFile, setImportFile] = useState<File | null>(null);
  const [isImporting, setIsImporting] = useState(false);
  const [importResult, setImportResult] = useState<ImportResult | null>(null);
  const [importError, setImportError] = useState<string>("");

  const { data: brands = [] } = useQuery<Brand[]>({
    queryKey: ["/api/admin/stations", "for-nodes"],
    queryFn: async () => {
      const res = await apiRequest<any, PagedResult<Brand>>("GET", "/api/admin/stations?pageSize=100");
      return res.items ?? [];
    },
  });

  const brandName = (id: string) => brands.find((b) => b.id === id)?.name ?? id;

  const { data: paged, isLoading } = useQuery<PagedResult<StationNode>>({
    queryKey: ["/api/admin/station-nodes", page, stationFilter],
    queryFn: async () => {
      const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
      if (stationFilter) params.set("stationId", stationFilter);
      return apiRequest<unknown, PagedResult<StationNode>>("GET", `/api/admin/station-nodes?${params.toString()}`);
    },
  });

  const nodes = paged?.items ?? [];
  const totalPages = paged?.totalPages ?? 1;

  const invalidate = () => queryClient.invalidateQueries({ queryKey: ["/api/admin/station-nodes"] });

  const createMutation = useMutation({
    mutationFn: (n: StationNode) => apiRequest("POST", "/api/admin/station-nodes", n),
    onSuccess: () => {
      invalidate();
      setIsCreating(false);
      setForm(EMPTY_FORM);
      toast.success(t("common.created"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const updateMutation = useMutation({
    mutationFn: (n: StationNode) => apiRequest("PUT", `/api/admin/station-nodes/${encodeURIComponent(n.id)}`, n),
    onSuccess: () => {
      invalidate();
      setEditingId(null);
      setForm(EMPTY_FORM);
      toast.success(t("common.saved"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => apiRequest("DELETE", `/api/admin/station-nodes/${encodeURIComponent(id)}`),
    onSuccess: () => {
      invalidate();
      toast.success(t("common.deleted"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  // Coordinate + required-field validation, mirrors the backend contract.
  const latNum = form.lat.trim() === "" ? NaN : parseFloat(form.lat);
  const lngNum = form.lng.trim() === "" ? NaN : parseFloat(form.lng);
  const coordsValid = isValidCoordinate(latNum, lngNum);
  const formValid = !!form.stationId.trim() && !!form.name.trim() && coordsValid;

  const startCreate = () => {
    setEditingId(null);
    setForm(EMPTY_FORM);
    setIsCreating(true);
  };

  const startEdit = (n: StationNode) => {
    setIsCreating(false);
    setEditingId(n.id);
    setForm(nodeToForm(n));
  };

  const cancelForm = () => {
    setIsCreating(false);
    setEditingId(null);
    setForm(EMPTY_FORM);
  };

  const autoDeriveId = () => {
    if (!form.stationId.trim() || !coordsValid) return;
    setForm((f) => ({ ...f, id: deriveNodeId(f.stationId.trim(), latNum, lngNum) }));
  };

  const submitForm = () => {
    if (!formValid) return;
    const payload = toNodePayload(form);
    if (editingId) {
      updateMutation.mutate({ ...payload, id: editingId });
    } else {
      // Single create needs an explicit id (the server does not derive it here).
      if (!payload.id) payload.id = deriveNodeId(payload.stationId, latNum, lngNum);
      createMutation.mutate(payload);
    }
  };

  const runImport = async () => {
    if (!importFile) return;
    setIsImporting(true);
    setImportError("");
    setImportResult(null);
    try {
      const fd = new FormData();
      fd.append("file", importFile);
      const res = await apiRequest<any, ImportResult>(
        "POST", "/api/admin/station-nodes/import", fd, undefined, 300_000, 0,
      );
      setImportResult({ added: res.added ?? 0, updated: res.updated ?? 0, errors: res.errors ?? [] });
      invalidate();
      setImportFile(null);
    } catch (e) {
      setImportError(e instanceof Error ? e.message : String(e));
    }
    setIsImporting(false);
  };

  const busy = createMutation.isPending || updateMutation.isPending || deleteMutation.isPending;
  const showForm = isCreating || editingId !== null;

  return (
    <div className="space-y-4">
      {/* Toolbar: brand filter · import · add */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Select
            value={stationFilter || "__all__"}
            onValueChange={(v) => { setStationFilter(v === "__all__" ? "" : v); setPage(1); }}
          >
            <SelectTrigger className="h-8 w-56">
              <SelectValue placeholder={t("stationNodes.allBrands")} />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="__all__">{t("stationNodes.allBrands")}</SelectItem>
              {brands.map((b) => (
                <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          {paged && (
            <span className="text-xs text-muted-foreground">
              {t("stationNodes.total")}: {paged.totalCount}
            </span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <Input
            type="file"
            accept=".csv,.json"
            className="hidden"
            id="node-import"
            onChange={(e) => {
              const f = e.target.files?.[0] ?? null;
              setImportFile(f);
              setImportResult(null);
              setImportError("");
              e.target.value = "";
            }}
          />
          <Button variant="outline" size="sm" onClick={() => document.getElementById("node-import")?.click()}>
            <FileUp className="w-3.5 h-3.5 mr-1" />
            {t("stationNodes.import")}
          </Button>
          <Button variant="outline" size="sm" onClick={startCreate}>
            <Plus className="w-3.5 h-3.5 mr-1" />
            {t("common.add")}
          </Button>
        </div>
      </div>

      {/* Pending import file → confirm */}
      {importFile && (
        <div className="bg-card border border-border rounded-xl p-4 flex items-center justify-between gap-3 flex-wrap animate-in fade-in slide-in-from-top-2 duration-200">
          <div className="text-sm">
            <span className="text-muted-foreground">{t("stationNodes.selectedFile")}: </span>
            <span className="font-mono text-primary">{importFile.name}</span>
            <span className="text-muted-foreground"> ({Math.round(importFile.size / 1024)} KB)</span>
            <p className="text-xs text-muted-foreground mt-1">{t("stationNodes.importHint")}</p>
          </div>
          <div className="flex items-center gap-2">
            <Button size="sm" onClick={runImport} disabled={isImporting}>
              {isImporting ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <FileUp className="w-3.5 h-3.5 mr-1" />}
              {t("stationNodes.upload")}
            </Button>
            <Button variant="ghost" size="sm" onClick={() => setImportFile(null)} disabled={isImporting}>
              <X className="w-3.5 h-3.5" />
            </Button>
          </div>
        </div>
      )}

      {/* Import outcome */}
      {importError && (
        <div className="bg-destructive/10 border border-destructive/30 rounded-xl p-4 text-sm text-destructive break-words">
          {importError}
        </div>
      )}
      {importResult && (
        <div className="bg-muted border border-border rounded-xl p-4 text-sm space-y-2">
          <div className="flex gap-4 flex-wrap">
            <span className="text-success">{t("stationNodes.importedAdded")}: {importResult.added}</span>
            <span className="text-info">{t("stationNodes.importedUpdated")}: {importResult.updated}</span>
            <span className="text-destructive">{t("stationNodes.importErrors")}: {importResult.errors.length}</span>
            <button onClick={() => setImportResult(null)} className="ml-auto text-xs text-muted-foreground hover:text-foreground underline">
              {t("import.close")}
            </button>
          </div>
          {importResult.errors.length > 0 && (
            <div className="max-h-48 overflow-y-auto space-y-1 pt-2 border-t border-border">
              {importResult.errors.map((er, i) => (
                <div key={i} className="text-xs flex gap-2">
                  <span className="text-muted-foreground font-mono shrink-0">#{er.line}</span>
                  <span className="text-destructive break-words">{er.message}</span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Create / edit form */}
      {showForm && (
        <div className="bg-card border border-border rounded-xl p-4 animate-in fade-in slide-in-from-top-2 duration-200">
          <div className="flex items-center justify-between mb-3">
            <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground flex items-center gap-2">
              <MapPin className="w-4 h-4 text-primary" />
              {editingId ? t("stationNodes.editNode") : t("stationNodes.addNode")}
            </h4>
          </div>
          <div className="flex items-end gap-3 flex-wrap">
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("stationNodes.brand")}</label>
              <Select value={form.stationId} onValueChange={(v) => setForm((f) => ({ ...f, stationId: v }))}>
                <SelectTrigger className="h-8 w-48"><SelectValue placeholder={t("stationNodes.brand")} /></SelectTrigger>
                <SelectContent>
                  {brands.map((b) => <SelectItem key={b.id} value={b.id}>{b.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("table.name")}</label>
              <Input value={form.name} onChange={(e) => setForm((f) => ({ ...f, name: e.target.value }))} className="h-8 w-56" placeholder={t("stationNodes.namePlaceholder")} />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("stationNodes.lat")}</label>
              <Input type="number" step="any" value={form.lat} onChange={(e) => setForm((f) => ({ ...f, lat: e.target.value }))} className="h-8 w-32 text-right" placeholder="50.45010" />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("stationNodes.lng")}</label>
              <Input type="number" step="any" value={form.lng} onChange={(e) => setForm((f) => ({ ...f, lng: e.target.value }))} className="h-8 w-32 text-right" placeholder="30.52340" />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("stationNodes.city")}</label>
              <Input value={form.city} onChange={(e) => setForm((f) => ({ ...f, city: e.target.value }))} className="h-8 w-40" />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("stationNodes.address")}</label>
              <Input value={form.address} onChange={(e) => setForm((f) => ({ ...f, address: e.target.value }))} className="h-8 w-64" />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("stationNodes.phone")}</label>
              <Input value={form.phone} onChange={(e) => setForm((f) => ({ ...f, phone: e.target.value }))} className="h-8 w-40" />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">{t("stationNodes.stationType")}</label>
              <Input value={form.stationType} onChange={(e) => setForm((f) => ({ ...f, stationType: e.target.value }))} className="h-8 w-40" />
            </div>
          </div>

          {/* Id row — required on create (server does not derive); fixed on edit.
              The numeric lat/lng above are the authoritative source; a visual map
              picker will sync into them in a follow-up without changing this contract. */}
          <div className="flex items-end gap-3 flex-wrap mt-3 pt-3 border-t border-border">
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">ID</label>
              <Input value={form.id} onChange={(e) => setForm((f) => ({ ...f, id: e.target.value }))} disabled={!!editingId} className="h-8 w-72 font-mono text-xs" placeholder={t("stationNodes.idHint")} />
            </div>
            {!editingId && (
              <Button variant="ghost" size="sm" className="h-8" onClick={autoDeriveId} disabled={!form.stationId.trim() || !coordsValid} title={t("stationNodes.autoIdHint")}>
                {t("stationNodes.autoId")}
              </Button>
            )}
            {!coordsValid && (form.lat.trim() !== "" || form.lng.trim() !== "") && (
              <span className="text-xs text-destructive flex items-center gap-1 pb-1.5">
                <AlertTriangle className="w-3 h-3" /> {t("stationNodes.invalidCoords")}
              </span>
            )}
            <div className="flex items-end gap-1 pb-0.5 ml-auto">
              <Button size="sm" className="h-8" onClick={submitForm} disabled={!formValid || busy}>
                {busy ? <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" /> : <Save className="w-3.5 h-3.5 mr-1" />}
                {editingId ? t("common.save") : t("common.create")}
              </Button>
              <Button variant="ghost" size="sm" className="h-8" onClick={cancelForm} disabled={busy}>
                <X className="w-3.5 h-3.5" />
              </Button>
            </div>
          </div>
        </div>
      )}

      {/* List */}
      {isLoading ? (
        <div className="flex items-center gap-2 text-muted-foreground p-8">
          <Loader2 className="w-5 h-5 animate-spin" />
          {t("common.loading")}
        </div>
      ) : nodes.length === 0 ? (
        <div className="text-center py-16 text-muted-foreground">
          <MapPin className="w-8 h-8 mx-auto mb-3 opacity-40" />
          <h3 className="text-lg font-medium mb-1">{t("common.noData")}</h3>
          <p className="text-sm">{t("stationNodes.noNodes")}</p>
        </div>
      ) : (
        <>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50">
                <tr>
                  <th className="text-left p-3 whitespace-nowrap">{t("table.name")}</th>
                  <th className="text-left p-3 whitespace-nowrap">{t("stationNodes.brand")}</th>
                  <th className="text-left p-3 whitespace-nowrap">{t("stationNodes.city")}</th>
                  <th className="text-right p-3 whitespace-nowrap">{t("stationNodes.coordinates")}</th>
                  <th className="text-center p-3 whitespace-nowrap">{t("common.actions")}</th>
                </tr>
              </thead>
              <tbody>
                {nodes.map((n) => (
                  <tr key={n.id} className="border-t border-border hover:bg-muted/20 transition-colors">
                    <td className="p-3">
                      <span className="font-medium">{n.name}</span>
                      {n.address && <div className="text-xs text-muted-foreground">{n.address}</div>}
                    </td>
                    <td className="p-3 text-muted-foreground">{brandName(n.stationId)}</td>
                    <td className="p-3 text-muted-foreground">{n.city ?? "—"}</td>
                    <td className="p-3 text-right tabular-nums text-xs text-muted-foreground whitespace-nowrap">
                      {n.lat != null && n.lng != null ? `${n.lat.toFixed(5)}, ${n.lng.toFixed(5)}` : "—"}
                    </td>
                    <td className="p-3">
                      <div className="flex justify-center gap-1">
                        {confirmDelete === n.id ? (
                          <div className="flex items-center gap-1 animate-in fade-in slide-in-from-right-2">
                            <Button size="sm" variant="destructive" className="h-8" disabled={deleteMutation.isPending}
                              onClick={() => { deleteMutation.mutate(n.id); setConfirmDelete(null); }}>
                              {deleteMutation.isPending ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Trash2 className="w-3.5 h-3.5 mr-1" />}
                              {t("common.delete")}
                            </Button>
                            <Button variant="ghost" size="sm" className="h-8" onClick={() => setConfirmDelete(null)}>
                              <X className="w-3.5 h-3.5" />
                            </Button>
                          </div>
                        ) : (
                          <>
                            <Button variant="ghost" size="sm" className="text-info hover:brightness-125 h-8" onClick={() => startEdit(n)}>
                              <Edit2 className="w-3.5 h-3.5" />
                            </Button>
                            <Button variant="ghost" size="sm" className="text-destructive hover:text-destructive h-8" onClick={() => setConfirmDelete(n.id)}>
                              <Trash2 className="w-3.5 h-3.5" />
                            </Button>
                          </>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="flex items-center justify-center gap-3 pt-2">
              <Button variant="outline" size="sm" className="h-8" disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))}>
                <ChevronLeft className="w-4 h-4" />
              </Button>
              <span className="text-sm text-muted-foreground tabular-nums">{page} / {totalPages}</span>
              <Button variant="outline" size="sm" className="h-8" disabled={page >= totalPages} onClick={() => setPage((p) => Math.min(totalPages, p + 1))}>
                <ChevronRight className="w-4 h-4" />
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

