import { Observable } from "rxjs";

// taken from https://medium.com/we-code/angular-and-json-streaming-388f33652102
export function streamWithFetch<T>(url: string) {
  return new Observable<T>(observer => {
    fetch(url).then(res => {
      const reader = res.body!.getReader();
      const gen = createStream<T>(reader);
      (async () => {
        while (true) {
          const { done, value } = await gen.next();
          if (done) {
            observer.complete();
            return;
          }
          observer.next(value);
        }
      })();
    });
  });
}

async function* createStream<T>(reader: ReadableStreamDefaultReader): AsyncGenerator<T, void> {
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { done, value } = await reader.read();

    if (done) {
      if (buffer.length > 0) {
        yield JSON.parse(buffer);
      }
      return;
    }

    buffer += decoder.decode(value, { stream: true });
    const lines = buffer.split(/\r?\n/);
    buffer = lines.pop()!;

    for (const line of lines) {
      yield JSON.parse(line);
    }
  }
}