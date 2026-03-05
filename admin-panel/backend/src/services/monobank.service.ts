import axios from 'axios';
import { AppError } from '../shared/errors/app-error';
import { logger } from '../infrastructure/logging/logger';

export interface MonobankInvoiceRequest {
    amount: number;
    ccy?: number; // Default 980 (UAH)
    merchantPaymentId: string;
    redirectUrl?: string;
    webHookUrl?: string;
    validity?: number;
    paymentType?: 'debit' | 'hold';
}

export interface MonobankInvoiceResponse {
    invoiceId: string;
    pageUrl: string;
}

export interface MonobankWebhookPayload {
    invoiceId: string;
    status: 'created' | 'processing' | 'hold' | 'success' | 'failure' | 'reversed' | 'expired';
    amount: number;
    ccy: number;
    finalAmount: number;
    createdDate: string;
    modifiedDate: string;
    reference: string;
    checksum: string;
}

export class MonobankService {
    private readonly baseUrl = 'https://api.monobank.ua/api/merchant';
    private readonly log = logger.child({ component: 'MonobankService' });

    constructor(private readonly apiToken: string) { }

    async createInvoice(request: MonobankInvoiceRequest): Promise<MonobankInvoiceResponse> {
        try {
            this.log.info({ merchantPaymentId: request.merchantPaymentId }, 'Creating Monobank invoice');

            const response = await axios.post(`${this.baseUrl}/invoice/create`, {
                amount: request.amount, // in kopecks
                ccy: request.ccy || 980,
                merchantPaymentId: request.merchantPaymentId,
                redirectUrl: request.redirectUrl,
                webHookUrl: request.webHookUrl,
            }, {
                headers: {
                    'X-Token': this.apiToken,
                }
            });

            return response.data;
        } catch (error: any) {
            const errorDetails = error.response?.data;
            this.log.error({
                message: error.message,
                details: errorDetails,
                merchantPaymentId: request.merchantPaymentId
            }, 'Failed to create Monobank invoice');

            throw AppError.internal(
                errorDetails?.errText || errorDetails?.errorDescription || 'Failed to initialize Monobank payment'
            );
        }
    }

    async getInvoiceStatus(invoiceId: string): Promise<any> {
        try {
            const response = await axios.get(`${this.baseUrl}/invoice/status?invoiceId=${invoiceId}`, {
                headers: {
                    'X-Token': this.apiToken,
                }
            });
            return response.data;
        } catch (error: any) {
            this.log.error({ invoiceId, error: error.message }, 'Failed to fetch Monobank invoice status');
            throw AppError.internal('Failed to fetch payment status');
        }
    }
}
