// What PDF.js's legacy build expects from the browser beyond what it fills in itself. Chromium 109 (the Legacy
// flavor) cannot iterate a ReadableStream with for await; PDF.js does so for text and some image streams.
if (typeof ReadableStream !== 'undefined' && !ReadableStream.prototype[Symbol.asyncIterator]) {
    ReadableStream.prototype[Symbol.asyncIterator] = async function* () {
        const reader = this.getReader();
        try {
            for (;;) {
                const { done, value } = await reader.read();
                if (done) return;
                yield value;
            }
        } finally {
            reader.releaseLock();
        }
    };
}
