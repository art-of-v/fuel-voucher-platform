import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Pencil, Plus, X, RotateCcw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { apiRequest, ApiError } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { toast } from "sonner";

interface Supplier {
    id: string;
    name: string;
    legalForm: string | null;
    phone: string | null;
    email: string | null;
    edrIpn: string | null;
    rnkrr: string | null;
    address: string | null;
    notes: string | null;
    isActive: boolean;
}

interface SupplierForm {
    name: string;
    legalForm: string;
    phone: string;
    email: string;
    edrIpn: string;
    rnkrr: string;
    address: string;
    notes: string;
}

const EMPTY: SupplierForm = {
    name: "", legalForm: "", phone: "", email: "", edrIpn: "", rnkrr: "", address: "", notes: "",
};

// A duplicate name comes back as 409 { code, message }. The code is the stable half, so localise from
// it and keep the backend's English sentence only for codes this screen has never seen.
const SUPPLIER_ERROR_KEYS: Record<string, string> = {
    supplier_name_taken: "suppliers.error.nameTaken",
};

/**
 * Suppliers — the external parties we buy vouchers from. A separate screen from "Providers", which is
 * the fuel brand, and separate from our own legal entities: a supplier is someone we settle with, and
 * the requisites differ by legal form (an ФОП has an ІПН, an LLC has an ЄДРОПУ), so nothing but the name
 * is required.
 */
export default function SuppliersTab() {
    const { t } = useI18n();
    const qc = useQueryClient();

    const { data: suppliers = [], isLoading } = useQuery({
        queryKey: ["suppliers", "all"],
        queryFn: () => apiRequest<any, Supplier[]>("GET", "/api/admin/suppliers?includeInactive=true"),
    });
    const { data: legalForms = [] } = useQuery({
        queryKey: ["suppliers", "legalForms"],
        queryFn: () => apiRequest<any, string[]>("GET", "/api/admin/suppliers/legal-forms"),
    });

    const [showInactive, setShowInactive] = useState(true);
    const [editing, setEditing] = useState<Supplier | null>(null);
    const [creating, setCreating] = useState(false);
    const [form, setForm] = useState<SupplierForm>(EMPTY);

    const invalidate = () => qc.invalidateQueries({ queryKey: ["suppliers"] });

    const save = useMutation({
        mutationFn: async () => {
            // Pass the object, not a pre-serialised string: apiRequest runs JSON.stringify(data) itself,
            // so handing it a string double-encodes the body and ASP.NET rejects it with
            // "One or more validation errors occurred" — a JSON string cannot bind to the request type.
            return editing
                ? apiRequest<unknown, Supplier>("PUT", `/api/admin/suppliers/${editing.id}`, form)
                : apiRequest<unknown, Supplier>("POST", "/api/admin/suppliers", form);
        },
        onSuccess: () => {
            invalidate();
            closeForm();
            toast.success(t("suppliers.saved"));
        },
        onError: (e: unknown) => {
            const code = e instanceof ApiError ? e.code : undefined;
            const key = code ? SUPPLIER_ERROR_KEYS[code] : undefined;
            toast.error(key ? t(key) : e instanceof Error ? e.message : String(e));
        },
    });

    const deactivate = useMutation({
        mutationFn: (id: string) => apiRequest<any, void>("DELETE", `/api/admin/suppliers/${id}`),
        onSuccess: () => { invalidate(); toast.success(t("suppliers.deactivated")); },
        onError: (e: unknown) => toast.error(e instanceof Error ? e.message : String(e)),
    });

    const reactivate = useMutation({
        mutationFn: (id: string) => apiRequest<any, Supplier>("POST", `/api/admin/suppliers/${id}/reactivate`),
        onSuccess: () => { invalidate(); toast.success(t("suppliers.reactivated")); },
        onError: (e: unknown) => toast.error(e instanceof Error ? e.message : String(e)),
    });

    const closeForm = () => {
        setEditing(null);
        setCreating(false);
        setForm(EMPTY);
    };

    const openEdit = (s: Supplier) => {
        setEditing(s);
        setCreating(false);
        setForm({
            name: s.name, legalForm: s.legalForm ?? "", phone: s.phone ?? "", email: s.email ?? "",
            edrIpn: s.edrIpn ?? "", rnkrr: s.rnkrr ?? "", address: s.address ?? "", notes: s.notes ?? "",
        });
    };

    const visible = showInactive ? suppliers : suppliers.filter((s) => s.isActive);

    const set = (key: keyof SupplierForm) => (v: string) => setForm((f) => ({ ...f, [key]: v }));

    return (
        <div className="space-y-4">
            <div className="flex items-center justify-between gap-4 flex-wrap">
                <p className="text-muted-foreground text-sm">{t("suppliers.description")}</p>
                <div className="flex items-center gap-2">
                    <label className="flex items-center gap-2 text-sm text-muted-foreground">
                        <input type="checkbox" checked={showInactive} onChange={(e) => setShowInactive(e.target.checked)} />
                        {t("suppliers.showInactive")}
                    </label>
                    <Button onClick={() => { setCreating(true); setEditing(null); setForm(EMPTY); }}>
                        <Plus className="w-4 h-4 mr-2" />{t("suppliers.add")}
                    </Button>
                </div>
            </div>

            {(creating || editing) && (
                <div className="glass-chrome rounded-xl p-4 space-y-3">
                    <div className="flex items-center justify-between">
                        <h3 className="font-semibold">{editing ? t("suppliers.edit") : t("suppliers.add")}</h3>
                        <Button variant="ghost" size="sm" onClick={closeForm}><X className="w-4 h-4" /></Button>
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                        <label className="text-sm">
                            {t("suppliers.name")}
                            <Input value={form.name} onChange={(e) => set("name")(e.target.value)} />
                        </label>
                        <label className="text-sm">
                            {t("suppliers.legalForm")}
                            <Select value={form.legalForm} onValueChange={set("legalForm")}>
                                <SelectTrigger><SelectValue placeholder={t("suppliers.legalFormPlaceholder")} /></SelectTrigger>
                                <SelectContent>
                                    {legalForms.map((f) => <SelectItem key={f} value={f}>{f}</SelectItem>)}
                                </SelectContent>
                            </Select>
                        </label>
                        <label className="text-sm">
                            {t("suppliers.phone")}
                            <Input value={form.phone} onChange={(e) => set("phone")(e.target.value)} />
                        </label>
                        <label className="text-sm">
                            {t("suppliers.email")}
                            <Input value={form.email} onChange={(e) => set("email")(e.target.value)} />
                        </label>
                        <label className="text-sm">
                            {t("suppliers.edrIpn")}
                            <Input value={form.edrIpn} onChange={(e) => set("edrIpn")(e.target.value)} />
                        </label>
                        <label className="text-sm">
                            {t("suppliers.rnkrr")}
                            <Input value={form.rnkrr} onChange={(e) => set("rnkrr")(e.target.value)} />
                        </label>
                        <label className="text-sm md:col-span-2">
                            {t("suppliers.address")}
                            <Input value={form.address} onChange={(e) => set("address")(e.target.value)} />
                        </label>
                        <label className="text-sm md:col-span-2">
                            {t("suppliers.notes")}
                            <Input value={form.notes} onChange={(e) => set("notes")(e.target.value)} />
                        </label>
                    </div>
                    <Button disabled={!form.name.trim() || save.isPending} onClick={() => save.mutate()}>
                        {save.isPending ? t("common.saving") : t("common.save")}
                    </Button>
                </div>
            )}

            <div className="glass-chrome rounded-xl overflow-hidden">
                <table className="w-full text-sm">
                    <thead>
                        <tr className="border-b border-border text-left">
                            <th className="p-3">{t("suppliers.name")}</th>
                            <th className="p-3">{t("suppliers.legalForm")}</th>
                            <th className="p-3">{t("suppliers.contacts")}</th>
                            <th className="p-3">{t("suppliers.requisites")}</th>
                            <th className="p-3">{t("suppliers.status")}</th>
                            <th className="p-3"></th>
                        </tr>
                    </thead>
                    <tbody>
                        {isLoading && <tr><td className="p-3" colSpan={6}>{t("common.loading")}</td></tr>}
                        {!isLoading && visible.length === 0 && (
                            <tr><td className="p-3" colSpan={6}>{t("suppliers.empty")}</td></tr>
                        )}
                        {visible.map((s) => (
                            <tr key={s.id} className="border-b border-border/50">
                                <td className="p-3 font-medium">{s.name}</td>
                                <td className="p-3">{s.legalForm ?? "—"}</td>
                                <td className="p-3">
                                    {s.phone ?? "—"}{s.phone && s.email ? " · " : ""}{s.email ?? ""}
                                </td>
                                <td className="p-3 text-muted-foreground">
                                    {s.edrIpn ?? "—"}{s.edrIpn && s.rnkrr ? " · " : ""}{s.rnkrr ?? ""}
                                </td>
                                <td className="p-3">
                                    {s.isActive
                                        ? <span className="text-green-500">{t("suppliers.active")}</span>
                                        : <span className="text-muted-foreground">{t("suppliers.inactive")}</span>}
                                </td>
                                <td className="p-3 flex gap-2 justify-end">
                                    <Button variant="outline" size="sm" onClick={() => openEdit(s)}>
                                        <Pencil className="w-3 h-3" />
                                    </Button>
                                    {s.isActive ? (
                                        <Button variant="outline" size="sm" disabled={deactivate.isPending}
                                            onClick={() => deactivate.mutate(s.id)}>
                                            {t("suppliers.deactivate")}
                                        </Button>
                                    ) : (
                                        <Button variant="outline" size="sm" disabled={reactivate.isPending}
                                            onClick={() => reactivate.mutate(s.id)}>
                                            <RotateCcw className="w-3 h-3" />
                                        </Button>
                                    )}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
}