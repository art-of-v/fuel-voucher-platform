import { useState } from "react";
import { useI18n } from "@/lib/i18n";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Plus,
  Trash2,
  Edit2,
  Save,
  X,
  Loader2,
  ChevronDown,
  ChevronRight,
  History,
  AlertTriangle,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { DecimalSettingInput } from "@/components/DecimalSettingInput";
import { FuelPriceHistory } from "@/components/FuelPriceHistory";
import CatalogueSyncButton from "@/components/CatalogueSyncButton";
import { apiRequest } from "@/lib/api-client";
import { resolveAllowBelowCost } from "@/lib/belowCostOptIn";
import { toast } from "sonner";
import {
  formatDateTime,
  localizeEventSummary,
  parseNominals,
} from "@/lib/utils";

interface BrandFuelDto {
  id: string;
  name: string;
  // NULLABLE, and it has to stay nullable. The cost is "not stated" until an operator says what
  // we pay - the catalogue import knows the column price and nothing about that - and a blank that
  // arrives as a 0 becomes a fuel priced at its margin with no below-cost guard.
  supplierPricePerLiter: number | null;
  marginUahPerLiter: number | null;
  marginPercent: number | null;
  finalPricePerLiter: number;
  pumpPricePerLiter: number | null;
  minDiscountPerLiter: number;
  discountPerLiter: number;
  allowBelowCost: boolean;
  /**
   * The fuel's code at the network ("A-95", "DP"). Null when it has none — and that is worth
   * seeing, because a fuel without a code is what the catalogue import can no longer match.
   */
  brandFuelCode: string | null;
  packageLiters: number[];
  /** Whether this fuel exists only to rehearse a flow. Decides whether its packages are sellable. */
  isTestData: boolean;
  /** Set only when this fuel's packages disagree; see BrandFuelPriceSpreadDto on the API. */
  priceSpread: {
    minSupplierPricePerLiter: number;
    maxSupplierPricePerLiter: number;
    minFinalPricePerLiter: number;
    maxFinalPricePerLiter: number;
  } | null;
}

interface BrandDto {
  id: string;
  /**
   * The network's own code ("okko"). This is what the API and the importers key on; `id` is the
   * uuid the database joins on and nothing outside it needs to know it. Server-assigned on create,
   * immutable afterwards — renaming it here would re-point every import file at nothing.
   */
  slug: string;
  name: string;
  logoText: string;
  color: string;
  sortOrder: number;
  fuels: BrandFuelDto[];
  nominals: number[];
}

interface AuditEventDto {
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
const DEFAULT_MIN_DISCOUNT = 0.5;

// Live preview mirror of the backend FuelPricing.FinalPerLiter formula:
// final/л = min(cost + profit, pump − minDiscount); a null/≤0 pump falls back to
// cost + profit (legacy cost-plus). The server is authoritative and recomputes on
// write — this only keeps the operator's Фінал column honest while editing.
function computeFinal(
  cost: number | null,
  profit: number | null,
  pump: number | null,
  minDiscount: number,
): number {
  // No cost means no price of ours: the column is the only figure we honestly have, and the
  // storefront shows that with "not in stock" next to it. Before this, a blank cost was a
  // ZERO here and in the payload, which priced the fuel at its margin.
  if (cost === null || profit === null) return pump ?? 0;
  const costPlus = cost + profit;
  if (pump && pump > 0) return Math.min(costPlus, pump - minDiscount);
  return costPlus;
}

interface BrandsTabProps {
  // Jumps to the Imports tab, where batch costs — the source of the derived supplier cost — are entered.
  onNavigateToImports?: () => void;
}

export default function BrandsTab({
  onNavigateToImports,
}: BrandsTabProps) {
  const { t } = useI18n();
  const queryClient = useQueryClient();
  const [expandedBrand, setExpandedBrand] = useState<string | null>(null);
  const [expandedHistory, setExpandedHistory] = useState<string | null>(null);
  const [editingFuel, setEditingFuel] = useState<string | null>(null);
  const [editValues, setEditValues] = useState<
    Record<string, Partial<BrandFuelDto>>
  >({});
  const [addingFuel, setAddingFuel] = useState<string | null>(null);
  const [newFuelName, setNewFuelName] = useState("");
  const [newFuelSupplierPrice, setNewFuelSupplierPrice] = useState("");
  const [newFuelMargin, setNewFuelMargin] = useState("");
  const [newFuelPump, setNewFuelPump] = useState("");
  const [newFuelMinDiscount, setNewFuelMinDiscount] = useState("0.50");
  const [newFuelAllowBelowCost, setNewFuelAllowBelowCost] = useState(false);
  const [newFuelBrandFuelCode, setNewFuelBrandFuelCode] = useState("");
  const [newFuelIsTestData, setNewFuelIsTestData] = useState(false);

  /**
   * The QA flag, with the same "edited value wins, otherwise the saved value" rule the
   * below-cost checkbox uses. `vals?.isTestData` is null until the operator touches it, and a
   * plain `??` would be indistinguishable from "the API sent nothing" - which is the bug this
   * control exists to end.
   */
  const isQaFuel = (edited: boolean | undefined, saved: boolean | undefined) =>
    edited ?? saved ?? false;
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null);

  const [isAddingBrand, setIsAddingBrand] = useState(false);
  const [newBrand, setNewBrand] = useState({
    name: "",
    logoText: "",
    color: DEFAULT_PROVIDER_COLOR,
    sortOrder: 999,
  });
  const [editingBrandId, setEditingBrandId] = useState<string | null>(
    null,
  );
  const [editBrand, setEditBrand] = useState({
    name: "",
    logoText: "",
    color: DEFAULT_PROVIDER_COLOR,
    sortOrder: 999,
  });
  const [confirmDeleteFuel, setConfirmDeleteFuel] = useState<string | null>(
    null,
  );

  const newFuelPumpNum =
    newFuelPump.trim() === "" ? null : parseFloat(newFuelPump) || 0;
  // A blank cost is "not stated", not zero. Zero would price the fuel at its margin and blind the
  // below-cost guard - and the fuel would sell for 2.75 UAH/litre.
  const newFuelCost = newFuelSupplierPrice.trim() === "" ? null : parseFloat(newFuelSupplierPrice);
  const newFuelFinalPrice = computeFinal(
    newFuelCost,
    newFuelMargin.trim() === "" ? null : parseFloat(newFuelMargin),
    newFuelPumpNum,
    parseFloat(newFuelMinDiscount) || 0,
  );
  const newFuelBelowCost = newFuelCost !== null && newFuelCost > 0 && newFuelFinalPrice < newFuelCost;

  const [editingNominals, setEditingNominals] = useState<string | null>(null);
  const [nominalInput, setNominalInput] = useState("");

  // Which fuel's price history is open underneath the table, if any. One at a time on purpose.
  const [priceHistoryFor, setPriceHistoryFor] = useState<string | null>(null);

  const { data: brands = [], isLoading } = useQuery<BrandDto[]>({
    queryKey: ["/api/admin/brands"],
  });

  const { data: history = [] } = useQuery<AuditEventDto[]>({
    queryKey: ["/api/admin/brands", expandedHistory, "history"],
    enabled: !!expandedHistory,
    queryFn: async () => {
      if (!expandedHistory) return [];
      const res = await apiRequest<any, AuditEventDto[]>(
        "GET",
        `/api/admin/brands/${expandedHistory}/history`,
      );
      return res;
    },
  });

  const createBrandMutation = useMutation({
    mutationFn: async (data: {
      slug: string;
      name: string;
      logoText: string;
      color: string;
      sortOrder: number;
    }) => {
      await apiRequest("POST", "/api/admin/brands", data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/brands"] });
      setIsAddingBrand(false);
      setNewBrand({
        name: "",
        logoText: "",
        color: DEFAULT_PROVIDER_COLOR,
        sortOrder: 999,
      });
      toast.success(t("common.created"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const handleCreateBrand = () => {
    const name = newBrand.name.trim();
    if (!name) return;
    // The slug is derived here, not on the server: it is the network's own code, and an operator
    // typing "OKKO" means "okko". The server owns the uuid and rejects a duplicate slug with a 409.
    const slug =
      name
        .toLowerCase()
        .replace(/[^a-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "")
        .slice(0, 100);
    if (!slug) return;
    createBrandMutation.mutate({
      slug,
      name,
      logoText: newBrand.logoText.trim(),
      color: newBrand.color,
      sortOrder: Number.isFinite(newBrand.sortOrder)
        ? newBrand.sortOrder
        : 999,
    });
  };

  const updateBrandMutation = useMutation({
    mutationFn: async ({
      id,
      data,
    }: {
      id: string;
      data: {
        slug: string;
        name: string;
        logoText: string;
        color: string;
        sortOrder: number;
      };
    }) => {
      await apiRequest("PUT", `/api/admin/brands/${id}`, data);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/brands"] });
      setEditingBrandId(null);
      toast.success(t("common.saved"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const startEditBrand = (brand: BrandDto) => {
    setEditingBrandId(brand.id);
    setEditBrand({
      name: brand.name,
      logoText: brand.logoText,
      color: brand.color,
      sortOrder: brand.sortOrder ?? 999,
    });
  };

  const handleSaveBrand = () => {
    const id = editingBrandId;
    if (!id || !editBrand.name.trim()) return;
    const brand = brands.find((b) => b.id === id);
    updateBrandMutation.mutate({
      id,
      data: {
        slug: brand?.slug ?? "",
        name: editBrand.name.trim(),
        logoText: editBrand.logoText.trim(),
        color: editBrand.color,
        sortOrder: Number.isFinite(editBrand.sortOrder)
          ? editBrand.sortOrder
          : 999,
      },
    });
  };

  const deleteBrandMutation = useMutation({
    mutationFn: async (id: string) => {
      await apiRequest("DELETE", `/api/admin/brands/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/brands"] });
      toast.success(t("common.deleted"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const updateFuelMutation = useMutation({
    mutationFn: async ({
      fuelId,
      data,
    }: {
      fuelId: string;
      data: BrandFuelDto;
    }) => {
      await apiRequest("PUT", `/api/admin/brand-fuels/${fuelId}`, data);
    },
    onSuccess: (_result, { fuelId }) => {
      // The history panel is keyed on the fuel's own URL, so that is what has to be invalidated -
      // the bare controller prefix is not a route, and this handler has no fuel of its own.
      queryClient.invalidateQueries({ queryKey: ["/api/admin/brands", `/api/admin/brand-fuels/${fuelId}`] });
      setEditingFuel(null);
      toast.success(t("common.saved"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const deleteFuelMutation = useMutation({
    mutationFn: async (fuelId: string) => {
      await apiRequest("DELETE", `/api/admin/brand-fuels/${fuelId}`);
    },
    onSuccess: (_result, fuelId) => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/brands", `/api/admin/brand-fuels/${fuelId}`] });
      toast.success(t("common.deleted"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const addFuelMutation = useMutation({
    mutationFn: async ({
      brandId,
      data,
    }: {
      brandId: string;
      data: Partial<BrandFuelDto>;
    }) => {
      await apiRequest(
        "POST",
        `/api/admin/brand-fuels/brands/${brandId}/fuels`,
        data,
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/brands"] });
      setAddingFuel(null);
      setNewFuelName("");
      setNewFuelSupplierPrice("");
      setNewFuelMargin("");
      setNewFuelPump("");
      setNewFuelMinDiscount("0.50");
      setNewFuelAllowBelowCost(false);
      setNewFuelIsTestData(false);
      setNewFuelBrandFuelCode("");
      toast.success(t("common.created"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const updateNominalsMutation = useMutation({
    mutationFn: async ({
      brandId,
      nominals,
    }: {
      brandId: string;
      nominals: number[];
    }) => {
      await apiRequest(
        "PUT",
        `/api/admin/brand-fuels/brands/${brandId}/nominals`,
        nominals,
      );
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["/api/admin/brands"] });
      setEditingNominals(null);
      toast.success(t("common.saved"));
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const startEditFuel = (fuel: BrandFuelDto) => {
    setEditingFuel(fuel.id);
    setEditValues({
      [fuel.id]: {
        name: fuel.name,
        supplierPricePerLiter: fuel.supplierPricePerLiter,
        marginUahPerLiter: fuel.marginUahPerLiter,
        marginPercent: fuel.marginPercent ?? undefined,
        pumpPricePerLiter: fuel.pumpPricePerLiter ?? null,
        minDiscountPerLiter: fuel.minDiscountPerLiter ?? DEFAULT_MIN_DISCOUNT,
        allowBelowCost: fuel.allowBelowCost ?? false,
        isTestData: fuel.isTestData ?? false,
        // Seeded from the saved value so the field reads as what it is. The save below always
        // sends a string, and the API treats "" as "clear" — which is what an operator emptying
        // the box means. An untouched box still round-trips to the same string.
        brandFuelCode: fuel.brandFuelCode ?? "",
      },
    });
  };

  const saveFuel = (fuel: BrandFuelDto) => {
    const vals = editValues[fuel.id];
    if (!vals) return;
    const supplier = vals.supplierPricePerLiter ?? fuel.supplierPricePerLiter;
    const margin = vals.marginUahPerLiter ?? fuel.marginUahPerLiter;
    // pumpPricePerLiter can legitimately be null (operator cleared it), so fall back to
    // the fuel's stored value only when the edit buffer never touched it (undefined).
    const pump =
      vals.pumpPricePerLiter !== undefined
        ? vals.pumpPricePerLiter
        : (fuel.pumpPricePerLiter ?? null);
    const minDiscount =
      vals.minDiscountPerLiter ??
      fuel.minDiscountPerLiter ??
      DEFAULT_MIN_DISCOUNT;
    // Server is authoritative and recomputes; send the previewed final for parity only.
    const finalPrice = computeFinal(supplier, margin, pump, minDiscount);
    updateFuelMutation.mutate({
      fuelId: fuel.id,
      data: {
        ...fuel,
        name: vals.name?.trim() || fuel.name,
        supplierPricePerLiter: supplier,
        marginUahPerLiter: margin,
        finalPricePerLiter: finalPrice,
        marginPercent: vals.marginPercent ?? fuel.marginPercent,
        pumpPricePerLiter: pump,
        minDiscountPerLiter: minDiscount,
        allowBelowCost: vals.allowBelowCost ?? fuel.allowBelowCost ?? false,
        isTestData: vals.isTestData ?? fuel.isTestData ?? false,
        // Always a string, never undefined: the API reads an omitted field as "leave it alone"
        // and "" as "clear it". Sending undefined here would make the code un-clearable from
        // this screen, since every save would look like the field had not been mentioned.
        brandFuelCode: vals.brandFuelCode ?? fuel.brandFuelCode ?? "",
      },
    });
  };

  const handleAddFuel = (brandId: string) => {
    if (!newFuelName || !newFuelFinalPrice) return;
    addFuelMutation.mutate({
      brandId,
      data: {
        name: newFuelName,
        // A blank cost is "not stated", not zero. Zero would price the fuel at its margin and
        // blind the below-cost guard.
        supplierPricePerLiter: newFuelSupplierPrice.trim() === "" ? null : parseFloat(newFuelSupplierPrice),
        marginUahPerLiter: newFuelMargin.trim() === "" ? null : parseFloat(newFuelMargin),
        finalPricePerLiter: newFuelFinalPrice,
        pumpPricePerLiter: newFuelPumpNum,
        minDiscountPerLiter: parseFloat(newFuelMinDiscount) || 0,
        allowBelowCost: newFuelAllowBelowCost,
        isTestData: newFuelIsTestData,
        brandFuelCode: newFuelBrandFuelCode.trim() || null,
      },
    });
  };

  const handleSaveNominals = (brand: BrandDto) => {
    updateNominalsMutation.mutate({
      brandId: brand.id,
      nominals: parseNominals(nominalInput),
    });
  };

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 text-muted-foreground p-8">
        <Loader2 className="w-5 h-5 animate-spin" />
        {t("common.loading")}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-end gap-2">
        <CatalogueSyncButton />

        <Button
          variant="outline"
          size="sm"
          onClick={() => setIsAddingBrand(true)}
        >
          <Plus className="w-3.5 h-3.5 mr-1" />
          {t("brands.addBrand")}
        </Button>
      </div>

      {isAddingBrand && (
        <div className="bg-card border border-border rounded-xl p-4 animate-in fade-in slide-in-from-top-2 duration-200">
          <div className="flex items-end gap-3 flex-wrap">
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                {t("table.name")}
              </label>
              <Input
                autoFocus
                placeholder={t("brands.namePlaceholder")}
                value={newBrand.name}
                onChange={(e) =>
                  setNewBrand((prev) => ({ ...prev, name: e.target.value }))
                }
                className="h-8 w-64"
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                {t("brands.logoText")}
              </label>
              <Input
                maxLength={6}
                placeholder={t("brands.logoTextPlaceholder")}
                value={newBrand.logoText}
                onChange={(e) =>
                  setNewBrand((prev) => ({
                    ...prev,
                    logoText: e.target.value,
                  }))
                }
                className="h-8 w-28"
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                {t("brands.color")}
              </label>
              <div className="flex items-center gap-2">
                <input
                  type="color"
                  value={newBrand.color}
                  onChange={(e) =>
                    setNewBrand((prev) => ({
                      ...prev,
                      color: e.target.value,
                    }))
                  }
                  className="h-8 w-12 cursor-pointer rounded bg-transparent border border-border"
                />
                <span className="text-xs font-mono text-muted-foreground">
                  {newBrand.color}
                </span>
              </div>
            </div>
            <div className="flex flex-col gap-1">
              <label
                className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider"
                title={t("brands.priorityHint")}
              >
                {t("brands.priority")}
              </label>
              <DecimalSettingInput
                type="number"
                inputMode="numeric"
                min={1}
                max={999}
                placeholder="999"
                value={newBrand.sortOrder}
                onCommit={(value) =>
                  setNewBrand((prev) => ({
                    ...prev,
                    sortOrder: Math.max(1, Math.round(value ?? 999)),
                  }))
                }
                className="h-8 w-20 text-right"
                aria-label={t("brands.priority")}
              />
            </div>
            <div className="flex items-end gap-1 pb-0.5 ml-auto">
              <Button
                size="sm"
                onClick={handleCreateBrand}
                disabled={
                  !newBrand.name.trim() || createBrandMutation.isPending
                }
                className="h-8"
              >
                {createBrandMutation.isPending ? (
                  <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
                ) : (
                  <Save className="w-3.5 h-3.5 mr-1" />
                )}
                {t("common.create")}
              </Button>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  setIsAddingBrand(false);
                  setNewBrand({
                    name: "",
                    logoText: "",
                    color: DEFAULT_PROVIDER_COLOR,
                    sortOrder: 999,
                  });
                }}
                disabled={createBrandMutation.isPending}
                className="h-8"
              >
                <X className="w-3.5 h-3.5" />
              </Button>
            </div>
          </div>
        </div>
      )}

      {editingBrandId &&
        (() => {
          const brand = brands.find((p) => p.id === editingBrandId);
          if (!brand) return null;
          return (
            <div className="bg-card border border-border rounded-xl p-4 animate-in fade-in slide-in-from-top-2 duration-200">
              <div className="flex items-center justify-between mb-3">
                <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">
                  {t("brands.editBrand")}
                </h4>
              </div>
              <div className="flex items-end gap-3 flex-wrap">
                <div className="flex flex-col gap-1">
                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                    {t("table.name")}
                  </label>
                  <Input
                    autoFocus
                    placeholder={t("brands.namePlaceholder")}
                    value={editBrand.name}
                    onChange={(e) =>
                      setEditBrand((prev) => ({
                        ...prev,
                        name: e.target.value,
                      }))
                    }
                    className="h-8 w-64"
                  />
                </div>
                <div className="flex flex-col gap-1">
                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                    {t("brands.logoText")}
                  </label>
                  <Input
                    maxLength={6}
                    placeholder={t("brands.logoTextPlaceholder")}
                    value={editBrand.logoText}
                    onChange={(e) =>
                      setEditBrand((prev) => ({
                        ...prev,
                        logoText: e.target.value,
                      }))
                    }
                    className="h-8 w-28"
                  />
                </div>
                <div className="flex flex-col gap-1">
                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                    {t("brands.color")}
                  </label>
                  <div className="flex items-center gap-2">
                    <input
                      type="color"
                      value={editBrand.color}
                      onChange={(e) =>
                        setEditBrand((prev) => ({
                          ...prev,
                          color: e.target.value,
                        }))
                      }
                      className="h-8 w-12 cursor-pointer rounded bg-transparent border border-border"
                    />
                    <span className="text-xs font-mono text-muted-foreground">
                      {editBrand.color}
                    </span>
                  </div>
                </div>
                <div className="flex flex-col gap-1">
                  <label
                    className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider"
                    title={t("brands.priorityHint")}
                  >
                    {t("brands.priority")}
                  </label>
                  <DecimalSettingInput
                    type="number"
                    inputMode="numeric"
                    min={1}
                    max={999}
                    value={editBrand.sortOrder}
                    onCommit={(value) =>
                      setEditBrand((prev) => ({
                        ...prev,
                        sortOrder: Math.max(1, Math.round(value ?? 999)),
                      }))
                    }
                    className="h-8 w-20 text-right"
                    aria-label={t("brands.priority")}
                  />
                </div>
                <div className="flex items-end gap-1 pb-0.5 ml-auto">
                  <Button
                    size="sm"
                    onClick={handleSaveBrand}
                    disabled={
                      !editBrand.name.trim() ||
                      updateBrandMutation.isPending
                    }
                    className="h-8"
                  >
                    {updateBrandMutation.isPending ? (
                      <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
                    ) : (
                      <Save className="w-3.5 h-3.5 mr-1" />
                    )}
                    {t("common.save")}
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setEditingBrandId(null)}
                    disabled={updateBrandMutation.isPending}
                    className="h-8"
                  >
                    <X className="w-3.5 h-3.5" />
                  </Button>
                </div>
              </div>
            </div>
          );
        })()}

      {brands.map((brand) => {
        const isExpanded = expandedBrand === brand.id;
        const isHistoryExpanded = expandedHistory === brand.id;
        const busy =
          updateFuelMutation.isPending ||
          deleteFuelMutation.isPending ||
          deleteBrandMutation.isPending;

        return (
          <div
            key={brand.id}
            className="bg-card border border-border rounded-xl overflow-hidden transition-all duration-200 hover:border-primary/20"
          >
            {/* Brand Header */}
            <div
              className="flex items-center justify-between p-4 cursor-pointer hover:bg-muted/50 transition-colors"
              onClick={() =>
                setExpandedBrand(isExpanded ? null : brand.id)
              }
            >
              <div className="flex items-center gap-3">
                <div
                  className="min-w-10 h-10 px-2.5 rounded-lg flex items-center justify-center text-lg font-bold text-black"
                  style={{ backgroundColor: brand.color }}
                >
                  {brand.logoText}
                </div>
                <div>
                  <h3 className="text-lg font-bold">{brand.name}</h3>
                  <p className="text-xs text-muted-foreground">
                    {brand.fuels.length} {t("nav.fueltypes")} ·{" "}
                    {t(
                      "brands.nominalsCount",
                      brand.nominals.length.toString(),
                    )}
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-2">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={(e) => {
                    e.stopPropagation();
                    startEditBrand(brand);
                  }}
                  className="text-info hover:brightness-125"
                >
                  <Edit2 className="w-4 h-4" />
                </Button>
                {confirmDelete === brand.id ? (
                  <div className="flex items-center gap-1 animate-in fade-in slide-in-from-right-2">
                    <span className="text-xs text-destructive font-medium flex items-center gap-1 mr-1">
                      <AlertTriangle className="w-3 h-3" />{" "}
                      {t("brands.deleteConfirm", brand.name)}
                    </span>
                    <Button
                      variant="destructive"
                      size="sm"
                      className="h-7 text-xs"
                      disabled={deleteBrandMutation.isPending}
                      onClick={(e) => {
                        e.stopPropagation();
                        deleteBrandMutation.mutate(brand.id);
                        setConfirmDelete(null);
                      }}
                    >
                      {deleteBrandMutation.isPending ? (
                        <Loader2 className="w-3 h-3 animate-spin" />
                      ) : (
                        <Trash2 className="w-3 h-3" />
                      )}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-7 text-xs"
                      onClick={(e) => {
                        e.stopPropagation();
                        setConfirmDelete(null);
                      }}
                    >
                      <X className="w-3 h-3" />
                    </Button>
                  </div>
                ) : (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={(e) => {
                      e.stopPropagation();
                      setConfirmDelete(brand.id);
                    }}
                    className="text-destructive hover:text-destructive"
                  >
                    {busy ? (
                      <Loader2 className="w-4 h-4 animate-spin" />
                    ) : (
                      <Trash2 className="w-4 h-4" />
                    )}
                  </Button>
                )}
                {isExpanded ? (
                  <ChevronDown className="w-5 h-5 text-muted-foreground" />
                ) : (
                  <ChevronRight className="w-5 h-5 text-muted-foreground" />
                )}
              </div>
            </div>

            {/* Expanded Content */}
            {isExpanded && (
              <div className="border-t border-border p-4 md:p-6 space-y-6 animate-in fade-in slide-in-from-top-2 duration-200">
                {/* Fuels Table */}
                <div>
                  <div className="flex items-center justify-between mb-3">
                    <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">
                      {t("price.title")}
                    </h4>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setAddingFuel(brand.id)}
                      disabled={addFuelMutation.isPending}
                    >
                      {addFuelMutation.isPending ? (
                        <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
                      ) : (
                        <Plus className="w-3.5 h-3.5 mr-1" />
                      )}
                      {t("common.add")}
                    </Button>
                  </div>

                  <div className="overflow-x-auto rounded-lg border border-border">
                    <table className="w-full text-sm">
                      <thead className="bg-muted/50">
                        <tr>
                          <th className="text-left p-3 whitespace-nowrap">
                            {t("table.name")}
                          </th>
                          <th className="text-right p-3 whitespace-nowrap">
                            <div>{t("price.supplier")}</div>
                            <div className="text-[10px] text-muted-foreground font-normal">
                              {t("price.unit")}
                            </div>
                          </th>
                          <th className="text-right p-3 whitespace-nowrap">
                            <div>{t("price.margin")}</div>
                            <div className="text-[10px] text-muted-foreground font-normal">
                              {t("price.unit")}
                            </div>
                          </th>
                          <th className="text-right p-3 whitespace-nowrap">
                            <div title={t("price.pumpHint")}>
                              {t("price.pump")}
                            </div>
                            <div className="text-[10px] text-muted-foreground font-normal">
                              {t("price.unit")}
                            </div>
                          </th>
                          <th className="text-right p-3 whitespace-nowrap">
                            <div title={t("brands.minDiscountHint")}>
                              {t("brands.minDiscount")}
                            </div>
                            <div className="text-[10px] text-muted-foreground font-normal">
                              {t("price.unit")}
                            </div>
                          </th>
                          <th className="text-right p-3 whitespace-nowrap">
                            <div>{t("price.final")}</div>
                            <div className="text-[10px] text-muted-foreground font-normal">
                              {t("price.unit")}
                            </div>
                          </th>
                          <th className="text-center p-3 whitespace-nowrap">
                            {t("table.nominals")}
                          </th>
                          <th className="text-center p-3 whitespace-nowrap">
                            {t("common.actions")}
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {brand.fuels.map((fuel) => {
                          const isEditing = editingFuel === fuel.id;
                          const vals = editValues[fuel.id];
                          const saving =
                            updateFuelMutation.isPending &&
                            editingFuel === fuel.id;
                          const deleting = deleteFuelMutation.isPending;
                          const editCost =
                            vals?.supplierPricePerLiter ??
                            fuel.supplierPricePerLiter;
                          const editProfit =
                            vals?.marginUahPerLiter ?? fuel.marginUahPerLiter;
                          const editPump =
                            vals && vals.pumpPricePerLiter !== undefined
                              ? vals.pumpPricePerLiter
                              : (fuel.pumpPricePerLiter ?? null);
                          const editMinDiscount =
                            vals?.minDiscountPerLiter ??
                            fuel.minDiscountPerLiter ??
                            DEFAULT_MIN_DISCOUNT;
                          const computedFinal = computeFinal(
                            editCost,
                            editProfit,
                            editPump,
                            editMinDiscount,
                          );
                          const shownFinal = isEditing
                            ? computedFinal
                            : fuel.finalPricePerLiter;
                          const shownCost = isEditing
                            ? editCost
                            : fuel.supplierPricePerLiter;
                          // A null cost is "we do not know what we paid", not "we paid nothing",
                          // so it is NOT a below-cost price - and the row shows the column beside it.
                          const belowCost =
                            shownCost !== null &&
                            shownCost > 0 &&
                            shownFinal !== null &&
                            shownFinal < shownCost;

                          return (
                            <tr
                              key={fuel.id}
                              className="border-t border-border hover:bg-muted/20 transition-colors"
                            >
                              <td className="p-3">
                                {isEditing ? (
                                  <Input
                                    value={vals?.name ?? ""}
                                    onChange={(e) =>
                                      setEditValues((prev) => ({
                                        ...prev,
                                        [fuel.id]: {
                                          ...prev[fuel.id],
                                          name: e.target.value,
                                        },
                                      }))
                                    }
                                    className="w-32 h-8 text-xs"
                                  />
                                ) : (
                                  <div className="flex flex-col gap-0.5">
                                    <span className="font-medium">
                                      {fuel.name}
                                    </span>
                                    {/* The code is what the catalogue import matches on. A fuel
                                        without one is invisible to the next sync tick, which is
                                        the usual reason a grade gets typed in twice. */}
                                    <span
                                      className="text-[10px] text-muted-foreground tabular-nums"
                                      title={t("fuel.codeHint")}
                                    >
                                      {fuel.brandFuelCode
                                        ? fuel.brandFuelCode
                                        : t("fuel.codeNone")}
                                    </span>
                                  </div>
                                )}
                                {isEditing && (
                                  <Input
                                    value={vals?.brandFuelCode ?? ""}
                                    placeholder={t("fuel.codePlaceholder")}
                                    title={t("fuel.codeHint")}
                                    onChange={(e) =>
                                      setEditValues((prev) => ({
                                        ...prev,
                                        [fuel.id]: {
                                          ...prev[fuel.id],
                                          brandFuelCode: e.target.value,
                                        },
                                      }))
                                    }
                                    className="w-32 h-7 text-[11px] mt-1"
                                  />
                                )}
                              </td>
                              <td className="p-3">
                                {/* Cost is derived (weighted average of batch costs) since pricing slice 2a, so
                                    it is read-only here — a manual edit would be silently reverted on the next
                                    batch reprice. Operators set it on the Imports tab → batch costs. */}
                                {isEditing ? (
                                  <div className="flex flex-col items-end gap-0.5">
                                    <span
                                      className="block text-right tabular-nums text-muted-foreground"
                                      title={t("price.supplierDerivedHint")}
                                    >
                                      {fuel.supplierPricePerLiter !== null
                                        ? fuel.supplierPricePerLiter.toFixed(2)
                                        : "—"}
                                    </span>
                                    <button
                                      type="button"
                                      onClick={onNavigateToImports}
                                      disabled={!onNavigateToImports}
                                      title={t("price.supplierDerivedHint")}
                                      className="text-[10px] text-muted-foreground underline decoration-dotted hover:text-foreground transition-colors disabled:no-underline disabled:cursor-default"
                                    >
                                      {t("price.supplierDerived")}
                                    </button>
                                  </div>
                                ) : (
                                  <span className="block text-right tabular-nums">
                                    {fuel.supplierPricePerLiter !== null
                                      ? fuel.supplierPricePerLiter.toFixed(2)
                                      : "—"}
                                  </span>
                                )}
                              </td>
                              <td className="p-3">
                                {isEditing ? (
                                  <DecimalSettingInput
                                    type="number"
                                    inputMode="decimal"
                                    step="0.01"
                                    value={vals?.marginUahPerLiter ?? 0}
                                    onCommit={(value) =>
                                      setEditValues((prev) => ({
                                        ...prev,
                                        [fuel.id]: {
                                          ...prev[fuel.id],
                                          marginUahPerLiter: value ?? 0,
                                        },
                                      }))
                                    }
className="w-24 h-8 text-right text-xs"
                                     aria-label={`${t("price.margin")} — ${fuel.name}`}
                                   />
                                ) : (
                                  <span className="block text-right tabular-nums text-primary">
                                    {fuel.marginUahPerLiter !== null
                                      ? fuel.marginUahPerLiter.toFixed(2)
                                      : "—"}
                                  </span>
                                )}
                              </td>
                              <td className="p-3">
                                {isEditing ? (
                                  <DecimalSettingInput
                                    type="number"
                                    inputMode="decimal"
                                    step="0.01"
                                    min={0}
                                    placeholder="—"
                                    value={vals?.pumpPricePerLiter ?? null}
                                    emptyValue={null}
                                    onCommit={(value) =>
                                      setEditValues((prev) => ({
                                        ...prev,
                                        [fuel.id]: {
                                          ...prev[fuel.id],
                                          pumpPricePerLiter: value,
                                        },
                                      }))
                                    }
                                    className="w-24 h-8 text-right text-xs"
                                    aria-label={`${t("price.pump")} — ${fuel.name}`}
                                  />
                                ) : (
                                  <span className="block text-right tabular-nums text-muted-foreground">
                                    {fuel.pumpPricePerLiter != null
                                      ? fuel.pumpPricePerLiter.toFixed(2)
                                      : "—"}
                                  </span>
                                )}
                              </td>
                              <td className="p-3">
                                {isEditing ? (
                                  <DecimalSettingInput
                                    type="number"
                                    inputMode="decimal"
                                    step="0.01"
                                    min={0}
                                    value={
                                      vals?.minDiscountPerLiter ??
                                      DEFAULT_MIN_DISCOUNT
                                    }
                                    onCommit={(value) =>
                                      setEditValues((prev) => ({
                                        ...prev,
                                        [fuel.id]: {
                                          ...prev[fuel.id],
                                          minDiscountPerLiter: Math.max(
                                            0,
                                            value ?? 0,
                                          ),
                                        },
                                      }))
                                    }
                                    className="w-24 h-8 text-right text-xs"
                                    aria-label={`${t("brands.minDiscount")} — ${fuel.name}`}
                                  />
                                ) : (
                                  <span className="block text-right tabular-nums text-muted-foreground">
                                    {(fuel.minDiscountPerLiter ?? 0).toFixed(2)}
                                  </span>
                                )}
                              </td>
                              <td className="p-3">
                                <div
                                  className={`flex items-center justify-end gap-1 ${isEditing ? "px-2 py-1.5 bg-primary/5 rounded" : ""}`}
                                >
                                  {belowCost && (
                                    <span
                                      className="inline-flex items-center gap-0.5 text-[10px] font-semibold text-destructive uppercase tracking-wide"
                                      title={t("price.belowCostHint")}
                                    >
                                      <AlertTriangle className="w-3 h-3" />{" "}
                                      {t("price.belowCost")}
                                    </span>
                                  )}
                                  {fuel.priceSpread && (
                                    <span
                                      className="inline-flex items-center gap-0.5 text-[10px] font-semibold text-amber-600 dark:text-amber-500 uppercase tracking-wide"
                                      title={t("price.spreadHint")}
                                    >
                                      <AlertTriangle className="w-3 h-3" />{" "}
                                      {t("price.spread")}
                                    </span>
                                  )}
                                  <span
                                    className={`text-right font-bold tabular-nums ${isEditing ? "text-primary text-sm" : belowCost ? "text-destructive" : ""}`}
                                  >
                                    {shownFinal.toFixed(2)}
                                  </span>
                                </div>
                                {/* The packages of this fuel disagree. Saving from this row rewrites every
                    one of them to what is shown here, so the operator is told what they are about to
                    flatten - and what the disagreement was - before they do it, not after. */}
                                {fuel.priceSpread && !isEditing && (
                                  <div className="mt-0.5 text-right text-[10px] text-muted-foreground tabular-nums">
                                    {t("price.spreadCost",
                                      fuel.priceSpread.minSupplierPricePerLiter.toFixed(2),
                                      fuel.priceSpread.maxSupplierPricePerLiter.toFixed(2),
                                    )}
                                  </div>
                                )}
                                {/* Always offered, not only once the price is already below cost. Arming it in
                    advance is the point: clearing near-expiry stock below cost is a legitimate
                    decision, and an operator who hits the renewal guard has to be able to find this
                    switch without first mispricing live fuel to reveal it.
                    `fuel.allowBelowCost` is the fallback for "untouched yet" — otherwise a fuel that
                    IS opted in renders as unchecked the moment the editor opens. */}
                {isEditing && (
                  <label
                    className={`mt-1.5 flex items-center justify-end gap-1.5 text-[10px] font-medium cursor-pointer ${belowCost || resolveAllowBelowCost(vals?.allowBelowCost, fuel.allowBelowCost) ? "text-destructive" : "text-muted-foreground"}`}
                    title={t("price.allowBelowCostHint")}
                  >
                    <input
                      type="checkbox"
                      checked={resolveAllowBelowCost(
                        vals?.allowBelowCost,
                        fuel.allowBelowCost,
                      )}
                      onChange={(e) =>
                        setEditValues((prev) => ({
                          ...prev,
                          [fuel.id]: {
                            ...prev[fuel.id],
                            allowBelowCost: e.target.checked,
                          },
                        }))
                      }
                      className="h-3 w-3 accent-destructive"
                    />
                    {t("price.allowBelowCost")}
                                  </label>
                                )}

                                {/* The rehearsal flag decides whether these packages are sellable,
                                    and until now the panel had no control for it at all: a fuel
                                    added here landed with the API's default (not a rehearsal),
                                    and an edit rewrote the flag on every package of the fuel from
                                    whatever the client happened to send. Shown always, not only
                                    while editing - a fuel that is silently flagged is exactly
                                    what an operator needs to be able to see. */}
                                <label
                                  className={`mt-1.5 flex items-center justify-end gap-1.5 text-[10px] font-medium cursor-pointer ${isQaFuel(vals?.isTestData, fuel.isTestData) ? "text-primary" : "text-muted-foreground"}`}
                                  title={t("price.isTestDataHint")}
                                >
                                  <input
                                    type="checkbox"
                                    checked={isQaFuel(
                                      vals?.isTestData,
                                      fuel.isTestData,
                                    )}
                                    onChange={(e) =>
                                      setEditValues((prev) => ({
                                        ...prev,
                                        [fuel.id]: {
                                          ...prev[fuel.id],
                                          isTestData: e.target.checked,
                                        },
                                      }))
                                    }
                                    className="h-3 w-3 accent-primary"
                                  />
                                  {isQaFuel(
                                    vals?.isTestData,
                                    fuel.isTestData,
                                  )
                                    ? t("price.isTestData")
                                    : t("price.live")}
                                </label>
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
                                      <Button
                                        size="sm"
                                        onClick={() => saveFuel(fuel)}
                                        disabled={saving}
                                        className="h-8"
                                      >
                                        {saving ? (
                                          <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
                                        ) : (
                                          <Save className="w-3.5 h-3.5 mr-1" />
                                        )}
                                        {t("common.save")}
                                      </Button>
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        onClick={() => setEditingFuel(null)}
                                        disabled={saving}
                                        className="h-8"
                                      >
                                        <X className="w-3.5 h-3.5" />
                                      </Button>
                                    </>
                                  ) : confirmDeleteFuel === fuel.id ? (
                                    <div className="flex items-center gap-1 animate-in fade-in slide-in-from-right-2">
                                      <Button
                                        size="sm"
                                        variant="destructive"
                                        disabled={deleting}
                                        className="h-8"
                                        onClick={() => {
                                          deleteFuelMutation.mutate(fuel.id);
                                          setConfirmDeleteFuel(null);
                                        }}
                                      >
                                        {deleting ? (
                                          <Loader2 className="w-3.5 h-3.5 animate-spin" />
                                        ) : (
                                          <Trash2 className="w-3.5 h-3.5 mr-1" />
                                        )}
                                        {t(
                                          "brands.deleteConfirm",
                                          fuel.name,
                                        )}
                                      </Button>
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        onClick={() =>
                                          setConfirmDeleteFuel(null)
                                        }
                                        disabled={deleting}
                                        className="h-8"
                                      >
                                        <X className="w-3.5 h-3.5" />
                                      </Button>
                                    </div>
                                  ) : (
                                    <>
                                      {/* The price history is a whole panel, not a field of this row, so it
                                          opens underneath rather than in a dialog: an operator comparing a
                                          price against the row that shows it today needs both on screen. */}
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        onClick={() =>
                                          setPriceHistoryFor((current) =>
                                            current === fuel.id ? null : fuel.id,
                                          )
                                        }
                                        title={t("priceHistory.openHint")}
                                        className="h-8"
                                      >
                                        <History className="w-3.5 h-3.5" />
                                      </Button>
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        onClick={() => startEditFuel(fuel)}
                                        className="text-info hover:brightness-125 h-8"
                                      >
                                        <Edit2 className="w-3.5 h-3.5" />
                                      </Button>
                                      <Button
                                        variant="ghost"
                                        size="sm"
                                        disabled={deleting}
                                        onClick={() =>
                                          setConfirmDeleteFuel(fuel.id)
                                        }
                                        className="text-destructive hover:text-destructive h-8"
                                      >
                                        {deleting ? (
                                          <Loader2 className="w-3.5 h-3.5 animate-spin" />
                                        ) : (
                                          <Trash2 className="w-3.5 h-3.5" />
                                        )}
                                      </Button>
                                    </>
                                  )}
                                </div>
                              </td>
                            </tr>
                          );
                        })}
                        {/* The open price history, as a full-width row under the fuel it belongs to.
                            One fuel at a time: two open panels side by side would answer neither. */}
                        {priceHistoryFor &&
                          brand.fuels.some((fuel) => fuel.id === priceHistoryFor) && (
                            <tr className="border-t border-border bg-muted/20">
                              <td colSpan={8} className="p-3">
                                <FuelPriceHistory fuelId={priceHistoryFor} />
                              </td>
                            </tr>
                          )}
                        {/* Add Fuel Row */}
                        {addingFuel === brand.id && (
                          <tr className="border-t border-border bg-muted/30">
                            <td colSpan={8} className="p-3">
                              <div className="flex items-end gap-3 flex-wrap">
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                                    {t("table.name")}
                                  </label>
                                  <Input
                                    placeholder="A-95"
                                    value={newFuelName}
                                    onChange={(e) =>
                                      setNewFuelName(e.target.value)
                                    }
                                    className="h-8 w-36"
                                  />
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                                    {t("fuel.code")}
                                  </label>
                                  <Input
                                    placeholder="A-95"
                                    value={newFuelBrandFuelCode}
                                    title={t("fuel.codeHint")}
                                    onChange={(e) =>
                                      setNewFuelBrandFuelCode(e.target.value)
                                    }
                                    className="h-8 w-24"
                                  />
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                                    {t("price.supplier")}, {t("price.unit")}
                                  </label>
                                  <Input
                                    type="number"
                                    step="0.01"
                                    placeholder="50.00"
                                    value={newFuelSupplierPrice}
                                    onChange={(e) =>
                                      setNewFuelSupplierPrice(e.target.value)
                                    }
                                    className="h-8 w-28 text-right"
                                  />
                                  {/* A brand-new fuel has no batch yet, so this seed is the only cost source
                                      until the first batch cost is recorded, after which it is recomputed. */}
                                  <span className="text-[10px] text-muted-foreground leading-tight w-28">
                                    {t("price.supplierSeedHint")}
                                  </span>
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                                    {t("price.margin")}, {t("price.unit")}
                                  </label>
                                  <Input
                                    type="number"
                                    step="0.01"
                                    placeholder="2.00"
                                    value={newFuelMargin}
                                    onChange={(e) =>
                                      setNewFuelMargin(e.target.value)
                                    }
                                    className="h-8 w-28 text-right"
                                  />
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label
                                    className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider"
                                    title={t("price.pumpHint")}
                                  >
                                    {t("price.pump")}, {t("price.unit")}
                                  </label>
                                  <Input
                                    type="number"
                                    step="0.01"
                                    min={0}
                                    placeholder="—"
                                    value={newFuelPump}
                                    onChange={(e) =>
                                      setNewFuelPump(e.target.value)
                                    }
                                    className="h-8 w-28 text-right"
                                  />
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label
                                    className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider"
                                    title={t("brands.minDiscountHint")}
                                  >
                                    {t("brands.minDiscount")},{" "}
                                    {t("price.unit")}
                                  </label>
                                  <Input
                                    type="number"
                                    step="0.01"
                                    min={0}
                                    placeholder="0.50"
                                    value={newFuelMinDiscount}
                                    onChange={(e) =>
                                      setNewFuelMinDiscount(e.target.value)
                                    }
                                    className="h-8 w-28 text-right"
                                  />
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                                    {t("price.final")}, {t("price.unit")}
                                  </label>
                                  <div
                                    className={`h-8 flex items-center justify-end gap-1 text-right font-bold tabular-nums text-sm rounded px-3 ${newFuelBelowCost ? "text-destructive bg-destructive/5" : "text-primary bg-primary/5"}`}
                                  >
                                    {newFuelBelowCost && (
                                      <span
                                        className="inline-flex items-center gap-0.5 text-[10px] font-semibold uppercase tracking-wide"
                                        title={t("price.belowCostHint")}
                                      >
                                        <AlertTriangle className="w-3 h-3" />{" "}
                                        {t("price.belowCost")}
                                      </span>
                                    )}
                                    {newFuelFinalPrice.toFixed(2)}
                                  </div>
                                </div>
                                {/* Same as the edit form: the opt-in is a standing permission, so it is
                                    offered whether or not the price currently sits below cost.
                                    Waiting for belowCost to reveal it meant a new fuel could never
                                    be pre-authorised at all. */}
                                <div className="flex flex-col gap-1">
                                  <label
                                    className={`text-[11px] font-medium uppercase tracking-wider ${newFuelBelowCost ? "text-destructive" : "text-muted-foreground"}`}
                                  >
                                    {t("price.belowCost")}
                                  </label>
                                  <label
                                    className={`h-8 flex items-center gap-1.5 text-xs font-medium cursor-pointer ${newFuelBelowCost || newFuelAllowBelowCost ? "text-destructive" : "text-muted-foreground"}`}
                                    title={t("price.allowBelowCostHint")}
                                  >
                                    <input
                                      type="checkbox"
                                      checked={newFuelAllowBelowCost}
                                      onChange={(e) =>
                                        setNewFuelAllowBelowCost(
                                          e.target.checked,
                                        )
                                      }
                                      className="h-3.5 w-3.5 accent-destructive"
                                    />
                                    {t("price.allowBelowCost")}
                                  </label>
                                  {/* Same control as on the edit row, and present for the same
                                      reason: a fuel added from here used to land with the API's
                                      default, which is "not a rehearsal" - i.e. on sale. */}
                                  <label
                                    className="flex items-center gap-1.5 text-[10px] font-medium cursor-pointer text-muted-foreground"
                                    title={t("price.isTestDataHint")}
                                  >
                                    <input
                                      type="checkbox"
                                      checked={newFuelIsTestData}
                                      onChange={(e) =>
                                        setNewFuelIsTestData(e.target.checked)
                                      }
                                      className="h-3.5 w-3.5 accent-primary"
                                    />
                                    {t("price.isTestData")}
                                  </label>
                                </div>
                                <div className="flex flex-col gap-1">
                                  <label className="text-[11px] text-muted-foreground font-medium uppercase tracking-wider">
                                    {t("table.nominals")}
                                  </label>
                                  <div className="h-8 flex items-center text-xs text-muted-foreground">
                                    {t("price.nominalsAuto")}
                                  </div>
                                </div>
                                <div className="flex items-end gap-1 pb-0.5">
                                  <Button
                                    size="sm"
                                    onClick={() => handleAddFuel(brand.id)}
                                    disabled={
                                      !newFuelName ||
                                      !newFuelFinalPrice ||
                                      addFuelMutation.isPending
                                    }
                                    className="h-8"
                                  >
                                    {addFuelMutation.isPending ? (
                                      <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
                                    ) : (
                                      <Plus className="w-3.5 h-3.5 mr-1" />
                                    )}
                                    {t("common.add")}
                                  </Button>
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={() => setAddingFuel(null)}
                                    disabled={addFuelMutation.isPending}
                                    className="h-8"
                                  >
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
                    <h4 className="font-semibold text-sm uppercase tracking-wider text-muted-foreground">
                      {t("table.nominals")}
                    </h4>
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={updateNominalsMutation.isPending}
                      onClick={() => {
                        setEditingNominals(brand.id);
                        setNominalInput(brand.nominals.join(", "));
                      }}
                    >
                      {updateNominalsMutation.isPending ? (
                        <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
                      ) : (
                        <Edit2 className="w-3.5 h-3.5 mr-1" />
                      )}
                      {t("common.edit")}
                    </Button>
                  </div>
                  {editingNominals === brand.id ? (
                    <div className="flex gap-2 items-center">
                      <Input
                        value={nominalInput}
                        onChange={(e) => setNominalInput(e.target.value)}
                        placeholder="2, 3, 5, 10, 20, 50, 100"
                        className="flex-1"
                      />
                      <Button
                        size="sm"
                        disabled={updateNominalsMutation.isPending}
                        onClick={() => handleSaveNominals(brand)}
                      >
                        {updateNominalsMutation.isPending ? (
                          <Loader2 className="w-4 h-4 mr-1 animate-spin" />
                        ) : (
                          <Save className="w-4 h-4 mr-1" />
                        )}
                        {t("common.save")}
                      </Button>
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => setEditingNominals(null)}
                        disabled={updateNominalsMutation.isPending}
                      >
                        <X className="w-4 h-4" />
                      </Button>
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-2">
                      {brand.nominals.map((n) => (
                        <span
                          key={n}
                          className="px-3 py-1 bg-muted rounded-full text-sm font-mono border border-border"
                        >
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
                    onClick={() =>
                      setExpandedHistory(isHistoryExpanded ? null : brand.id)
                    }
                  >
                    <History className="w-4 h-4" />
                    <span className="text-sm font-medium">
                      {t("common.history")}
                    </span>
                    {isHistoryExpanded ? (
                      <ChevronDown className="w-4 h-4" />
                    ) : (
                      <ChevronRight className="w-4 h-4" />
                    )}
                  </div>
                  {isHistoryExpanded && (
                    <div className="mt-2 max-h-64 overflow-y-auto space-y-1 animate-in fade-in slide-in-from-top-1 duration-150">
                      {history.length === 0 ? (
                        <p className="text-sm text-muted-foreground p-2">
                          {t("common.noData")}
                        </p>
                      ) : (
                        history.map((evt) => (
                          <div
                            key={evt.id}
                            className="flex items-start gap-2 p-2 rounded bg-muted/30 text-sm hover:bg-muted/50 transition-colors"
                          >
                            <div
                              className={`w-1.5 h-1.5 rounded-full mt-1.5 shrink-0 ${
                                evt.eventType === "BrandCreated"
                                  ? "bg-success"
                                  : evt.eventType === "BrandDeleted"
                                    ? "bg-destructive"
                                    : evt.eventType === "PriceChanged"
                                      ? "bg-warning"
                                      : evt.eventType === "FuelAdded"
                                        ? "bg-info"
                                        : evt.eventType === "FuelRemoved"
                                          ? "bg-destructive"
                                          : "bg-info"
                              }`}
                            />
                            <div className="flex-1 min-w-0">
                              <p className="text-xs font-medium">
                                {localizeEventSummary(evt.summary, t)}
                              </p>
                              <p className="text-xs text-muted-foreground">
                                {evt.changedByUserName ?? "System"} ·{" "}
                                {formatDateTime(evt.changedAtUtc)}
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
      {brands.length === 0 && !isLoading && (
        <div className="text-center py-16 text-muted-foreground">
          <h3 className="text-lg font-medium mb-1">{t("common.noData")}</h3>
          <p className="text-sm mb-4">{t("brands.noBrands")}</p>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setIsAddingBrand(true)}
          >
            <Plus className="w-3.5 h-3.5 mr-1" />
            {t("brands.addBrand")}
          </Button>
        </div>
      )}
    </div>
  );
}
