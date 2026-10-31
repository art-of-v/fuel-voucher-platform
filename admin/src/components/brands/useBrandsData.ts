import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { apiRequest } from "@/lib/api-client";
import { computeFinal } from "@/lib/finalPrice";
import { parseNominals } from "@/lib/utils";
import { toast } from "sonner";
import { useI18n } from "@/lib/i18n";
import { codedErrorMessage } from "@/lib/coded-error-message";
import { BRAND_REFUSALS } from "./brandRefusals";
import {
  emptyBrandForm,
  type AuditEventDto,
  type BrandDto,
  type BrandFormValues,
  type BrandFuelDto,
} from "./types";

export function useBrandsData() {
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
  const [newBrand, setNewBrand] = useState<BrandFormValues>(emptyBrandForm);
  const [editingBrandId, setEditingBrandId] = useState<string | null>(null);
  const [editBrand, setEditBrand] = useState<BrandFormValues>(emptyBrandForm);
  const [confirmDeleteFuel, setConfirmDeleteFuel] = useState<string | null>(
    null,
  );

  const newFuelPumpNum =
    newFuelPump.trim() === "" ? null : parseFloat(newFuelPump) || 0;
  // A blank cost is "not stated", not zero. Zero would price the fuel at its margin and blind the
  // below-cost guard - and the fuel would sell for 2.75 UAH/litre.
  const newFuelCost =
    newFuelSupplierPrice.trim() === ""
      ? null
      : parseFloat(newFuelSupplierPrice);
  const newFuelFinalPrice = computeFinal(
    newFuelCost,
    newFuelMargin.trim() === "" ? null : parseFloat(newFuelMargin),
    newFuelPumpNum,
  );
  const newFuelBelowCost =
    newFuelCost !== null && newFuelCost > 0 && newFuelFinalPrice < newFuelCost;

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
      const res = await apiRequest<void, AuditEventDto[]>(
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
      setNewBrand(emptyBrandForm());
      toast.success(t("common.created"));
    },
    onError: (e: Error) => toast.error(codedErrorMessage(e, t, BRAND_REFUSALS)),
  });

  const handleCreateBrand = () => {
    const name = newBrand.name.trim();
    if (!name) return;
    // The slug is derived here, not on the server: it is the network's own code, and an operator
    // typing "OKKO" means "okko". The server owns the uuid and rejects a duplicate slug with a 409.
    const slug = name
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
      sortOrder: Number.isFinite(newBrand.sortOrder) ? newBrand.sortOrder : 999,
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
    onError: (e: Error) => toast.error(codedErrorMessage(e, t, BRAND_REFUSALS)),
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
    onError: (e: Error) => toast.error(codedErrorMessage(e, t, BRAND_REFUSALS)),
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
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/brands", `/api/admin/brand-fuels/${fuelId}`],
      });
      setEditingFuel(null);
      toast.success(t("common.saved"));
    },
    onError: (e: Error) => toast.error(codedErrorMessage(e, t, BRAND_REFUSALS)),
  });

  const deleteFuelMutation = useMutation({
    mutationFn: async (fuelId: string) => {
      await apiRequest("DELETE", `/api/admin/brand-fuels/${fuelId}`);
    },
    onSuccess: (_result, fuelId) => {
      queryClient.invalidateQueries({
        queryKey: ["/api/admin/brands", `/api/admin/brand-fuels/${fuelId}`],
      });
      toast.success(t("common.deleted"));
    },
    onError: (e: Error) => toast.error(codedErrorMessage(e, t, BRAND_REFUSALS)),
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
      setNewFuelAllowBelowCost(false);
      setNewFuelIsTestData(false);
      setNewFuelBrandFuelCode("");
      toast.success(t("common.created"));
    },
    onError: (e: Error) => toast.error(codedErrorMessage(e, t, BRAND_REFUSALS)),
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
    onError: (e: Error) => toast.error(codedErrorMessage(e, t, BRAND_REFUSALS)),
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
    // Server is authoritative and recomputes; send the previewed final for parity only.
    const finalPrice = computeFinal(supplier, margin, pump);
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
        supplierPricePerLiter:
          newFuelSupplierPrice.trim() === ""
            ? null
            : parseFloat(newFuelSupplierPrice),
        marginUahPerLiter:
          newFuelMargin.trim() === "" ? null : parseFloat(newFuelMargin),
        finalPricePerLiter: newFuelFinalPrice,
        pumpPricePerLiter: newFuelPumpNum,
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

  return {
    brands,
    isLoading,
    history,
    expandedBrand,
    setExpandedBrand,
    expandedHistory,
    setExpandedHistory,
    editingFuel,
    setEditingFuel,
    editValues,
    setEditValues,
    addingFuel,
    setAddingFuel,
    newFuelName,
    setNewFuelName,
    newFuelSupplierPrice,
    setNewFuelSupplierPrice,
    newFuelMargin,
    setNewFuelMargin,
    newFuelPump,
    setNewFuelPump,
    newFuelAllowBelowCost,
    setNewFuelAllowBelowCost,
    newFuelBrandFuelCode,
    setNewFuelBrandFuelCode,
    newFuelIsTestData,
    setNewFuelIsTestData,
    isQaFuel,
    confirmDelete,
    setConfirmDelete,
    isAddingBrand,
    setIsAddingBrand,
    newBrand,
    setNewBrand,
    editingBrandId,
    setEditingBrandId,
    editBrand,
    setEditBrand,
    confirmDeleteFuel,
    setConfirmDeleteFuel,
    newFuelPumpNum,
    newFuelCost,
    newFuelFinalPrice,
    newFuelBelowCost,
    editingNominals,
    setEditingNominals,
    nominalInput,
    setNominalInput,
    priceHistoryFor,
    setPriceHistoryFor,
    createBrandMutation,
    updateBrandMutation,
    deleteBrandMutation,
    updateFuelMutation,
    deleteFuelMutation,
    addFuelMutation,
    updateNominalsMutation,
    handleCreateBrand,
    startEditBrand,
    handleSaveBrand,
    startEditFuel,
    saveFuel,
    handleAddFuel,
    handleSaveNominals,
  };
}

export type BrandsController = ReturnType<typeof useBrandsData>;
