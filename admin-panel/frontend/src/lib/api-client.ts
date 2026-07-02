export const apiRequest = async <T, R = unknown>(
    method: string,
    url: string,
    data?: T,
    customHeaders?: Record<string, string>
): Promise<R> => {
    const headers: Record<string, string> = {
        "Content-Type": "application/json",
        ...customHeaders,
    };

    let body: BodyInit | undefined;
    if (data instanceof FormData) {
        delete headers["Content-Type"];
        body = data;
    } else if (data) {
        body = JSON.stringify(data);
    }

    const fullUrl = url.startsWith('http') ? url : getApiUrl(url);

    const response = await fetch(fullUrl, {
        method,
        headers,
        body,
        credentials: 'include',
    });

    if (!response.ok) {
        const errorText = await response.text();
        let errorMessage = errorText;

        try {
            const errorData = JSON.parse(errorText);
            if (errorData?.message) errorMessage = errorData.message;
        } catch {}

        throw new Error(errorMessage);
    }

    return response.json();
};
