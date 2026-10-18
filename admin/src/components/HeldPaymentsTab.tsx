import { useMemo, useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { RefreshCw } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { apiRequest } from "@/lib/api-client";
import { useI18n } from "@/lib/i18n";
import { toast } from "sonner";

interface HeldPayment {
    id: string;
    amountUah: number;
    currency: string;
    paidAtUtc: string | null;
    provider: string;
    userId: string | null;
    userPhone: string | null;
    userName: string | null;
    declaredCode: string | null;
    declaredText: string | null;
    declaredBy: string | null;
}

interface OpenOrder {
    id: string;
    userId: string;
    price: number;
    createdAtUtc: string;
}

interface HeldPaymentsResponse {
    data: HeldPayment[];
    total: number;
    openOrders: OpenOrder[];
}

interface CustomerHit {
    id: string;
    phoneNumber: string | null;
    settlementCode: string;
    name: string | null;
}

/**
 * Money we hold that is not yet against a sale.
 *
 * <p>Two jobs, and the screen exists for both. A payment with no customer is somebody's, and the
 * operator says whose — normally by re-checking the code finance typed, because the two common causes
 * are a mistyped code and a customer who has not registered, and they need different answers. A
 * payment with a customer but no order is waiting for an order, and the operator picks one from that
 * customer's open orders rather than assembling a UUID.
 *
 * <p>Nothing on this screen guesses. The backend refuses to attach money to another customer's
 * order or to a customer it already names, and there is no "best match" here to try first.
 */
export default function HeldPaymentsTab() {
    const { t } = useI18n();
    const qc = useQueryClient();

    const [codeLookup, setCodeLookup] = useState("");
    const [lookedUp, setLookedUp] = useState<CustomerHit | null | undefined>(undefined);
    const [orderChoice, setOrderChoice] = useState<Record<string, string>>({});

    const { data, isLoading, isFetching, refetch } = useQuery({
        queryKey: ["admin", "heldPayments"],
        queryFn: () => apiRequest<any, HeldPaymentsResponse>("GET", "/api/admin/payments/unallocated?limit=200"),
    });

    const invalidate = () => {
        qc.invalidateQueries({ queryKey: ["admin", "heldPayments"] });
    };

    const attach = useMutation({
        mutationFn: (body: { paymentId: string; orderId?: string; userId?: string }) =>
            apiRequest<unknown, void>(
                "POST",
                `/api/admin/payments/${body.paymentId}/attach`,
                { orderId: body.orderId ?? null, userId: body.userId ?? null },
            ),
        onSuccess: invalidate,
        // ApiError.message already carries the server's sentence — see api-client — so the refusal
        // the operator most needs ("that order belongs to a different customer") is shown as-is
        // rather than being replaced with "something went wrong".
        onError: (e: unknown) => toast.error(e instanceof Error ? e.message : String(e)),
    });

    const lookup = useMutation({
        mutationFn: () => apiRequest<unknown, CustomerHit>(
            "GET", `/api/admin/payments/customers/by-code?code=${encodeURIComponent(codeLookup.trim())}`),
        onSuccess: (customer) => {
            setLookedUp(customer);
            toast.success(t("heldPayments.codeFound"));
        },
        // 404 carries the explanation the endpoint wrote — mistyped, or not registered yet — and those
        // are different problems, so it is surfaced rather than flattened into a failure.
        onError: (e: unknown) => {
            setLookedUp(null);
            toast.error(e instanceof Error ? e.message : String(e));
        },
    });

    const openOrdersByUser = useMemo(() => {
        const grouped = new Map<string, OpenOrder[]>();
        for (const order of data?.openOrders ?? []) {
            grouped.set(order.userId, [...(grouped.get(order.userId) ?? []), order]);
        }
        return grouped;
    }, [data?.openOrders]);

    const rows = data?.data ?? [];

    return (
        <div className="space-y-6 animate-in fade-in duration-300">
            <div className="glass-panel p-6 space-y-3">
                <div className="flex items-center justify-between gap-4">
                    <h3 className="font-semibold">{t("heldPayments.title")}</h3>
                    <Button variant="ghost" size="sm" onClick={() => refetch()} disabled={isFetching}>
                        <RefreshCw className="w-4 h-4" />
                    </Button>
                </div>
                <p className="text-sm text-muted-foreground">{t("heldPayments.explainer")}</p>

                {/* The lookup for money declared with a code that matched nobody. */}
                <div className="flex flex-wrap items-end gap-2 pt-2">
                    <label className="text-sm flex-1 min-w-64">
                        {t("heldPayments.codeLookup")}
                        <Input
                            value={codeLookup}
                            onChange={(e) => { setCodeLookup(e.target.value.toUpperCase()); setLookedUp(undefined); }}
                            placeholder="K7P2M4XB"
                            className="font-mono"
                            maxLength={8}
                        />
                    </label>
                    <Button onClick={() => lookup.mutate()} disabled={lookup.isPending || codeLookup.trim().length !== 8}>
                        {t("heldPayments.find")}
                    </Button>
                </div>

                {lookedUp !== undefined && (
                    lookedUp === null
                        ? <p className="text-sm text-muted-foreground">{t("heldPayments.codeNotFound")}</p>
                        : (
                            <div className="text-sm">
                                <span className="text-muted-foreground">{t("heldPayments.codeBelongsTo")}: </span>
                                <span className="font-semibold">{lookedUp.name || t("heldPayments.noName")}</span>
                                {lookedUp.phoneNumber && <span className="text-muted-foreground"> · {lookedUp.phoneNumber}</span>}
                                <span className="text-muted-foreground font-mono"> · {lookedUp.id}</span>
                            </div>
                        )
                )}
            </div>

            <div className="glass-panel overflow-x-auto">
                <table className="w-full text-sm">
                    <thead>
                        <tr className="bg-muted">
                            <th className="text-left p-4">{t("heldPayments.amount")}</th>
                            <th className="text-left p-4">{t("heldPayments.paidAt")}</th>
                            <th className="text-left p-4">{t("heldPayments.customer")}</th>
                            <th className="text-left p-4">{t("heldPayments.declared")}</th>
                            <th className="text-left p-4">{t("heldPayments.place")}</th>
                        </tr>
                    </thead>
                    <tbody>
                        {isLoading && <tr><td className="p-4" colSpan={5}>{t("common.loading")}</td></tr>}
                        {!isLoading && rows.length === 0 && (
                            <tr><td className="p-4 text-muted-foreground" colSpan={5}>{t("heldPayments.empty")}</td></tr>
                        )}
                        {rows.map((payment) => {
                            const orders = payment.userId ? (openOrdersByUser.get(payment.userId) ?? []) : [];
                            const chosen = orderChoice[payment.id] ?? "";
                            const matching = chosen && orders.find(o => o.id === chosen)?.price === payment.amountUah;
                            const busy = attach.isPending;

                            return (
                                <tr key={payment.id} className="border-border hover:bg-foreground/5 transition-colors">
                                    <td className="p-4 font-bold text-foreground whitespace-nowrap">
                                        {payment.amountUah.toFixed(2)} {payment.currency}
                                    </td>
                                    <td className="p-4 text-muted-foreground whitespace-nowrap">
                                        {payment.paidAtUtc ? new Date(payment.paidAtUtc).toLocaleString() : "—"}
                                    </td>
                                    <td className="p-4">
                                        {payment.userId ? (
                                            <div>
                                                <div>{payment.userName || t("heldPayments.noName")}</div>
                                                {payment.userPhone && <div className="text-xs text-muted-foreground">{payment.userPhone}</div>}
                                            </div>
                                        ) : (
                                            <span className="text-destructive font-semibold">{t("heldPayments.unknown")}</span>
                                        )}
                                    </td>
                                    <td className="p-4 text-muted-foreground">
                                        {payment.declaredCode && (
                                            <div className="font-mono text-xs">{payment.declaredCode}</div>
                                        )}
                                        {payment.declaredText && (
                                            <div className="text-xs italic">“{payment.declaredText}”</div>
                                        )}
                                        {payment.declaredBy && (
                                            <div className="text-xs">{t("heldPayments.by")} {payment.declaredBy}</div>
                                        )}
                                    </td>
                                    <td className="p-4">
                                        {!payment.userId ? (
                                            <div className="space-y-1">
                                                <p className="text-xs text-muted-foreground">{t("heldPayments.needsOwner")}</p>
                                                {lookedUp && (
                                                    <Button
                                                        size="sm"
                                                        disabled={busy}
                                                        onClick={() => attach.mutate({ paymentId: payment.id, userId: lookedUp.id })}
                                                    >
                                                        {t("heldPayments.giveTo")} {lookedUp.name || t("heldPayments.noName")}
                                                    </Button>
                                                )}
                                            </div>
                                        ) : orders.length === 0 ? (
                                            <span className="text-xs text-muted-foreground">{t("heldPayments.noOpenOrder")}</span>
                                        ) : (
                                            <div className="flex items-center gap-2">
                                                <select
                                                    className="border border-input bg-background rounded-md px-2 py-1 text-xs"
                                                    value={chosen}
                                                    onChange={(e) => setOrderChoice((c) => ({ ...c, [payment.id]: e.target.value }))}
                                                >
                                                    <option value="">{t("heldPayments.pickOrder")}</option>
                                                    {orders.map((o) => (
                                                        <option key={o.id} value={o.id}>
                                                            {new Date(o.createdAtUtc).toLocaleString()} · {o.price.toFixed(2)} {payment.currency}
                                                        </option>
                                                    ))}
                                                </select>
                                                <Button
                                                    size="sm"
                                                    disabled={busy || !chosen}
                                                    onClick={() => attach.mutate({ paymentId: payment.id, orderId: chosen })}
                                                >
                                                    {t("heldPayments.attach")}
                                                </Button>
                                                {chosen && !matching && (
                                                    <span className="text-xs text-muted-foreground">{t("heldPayments.amountDiffers")}</span>
                                                )}
                            </div>
                                        )}
                                    </td>
                                </tr>
                            );
                        })}
                    </tbody>
                </table>
            </div>
        </div>
    );
}