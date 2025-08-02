import { HttpClient, HttpDownloadProgressEvent, HttpEventType, httpResource, HttpResponse } from '@angular/common/http';
import {Component, computed, effect, inject, resource, ResourceStreamItem, signal, WritableSignal } from '@angular/core';

@Component({
  selector: 'app-home',
  template: `
    <div>
      @for (message of this.history(); track $index) {
        <pre class="generated-response">{{ message.role }}: {{ message.text }}</pre>
      }

      @if (this.stream.isLoading() || this.remoteHistory.isLoading()) {
        <div class="loading-indicator">Loading...</div>
      }
      @let status = this.stream.status();
      @if (status === 'error') {
        <div class="error-indicator">
          <span>Error</span>
          {{ this.stream.error()?.message }}
        </div>
      } @else {
        @let value = this.stream.value();
        @if (value) {
          <pre class="generated-response">{{ value }}</pre>
        }
      }

      <input type="text" #queryInput placeholder="Enter your query..." (keydown.enter)="this.query.set(queryInput.value); queryInput.value = ''"/>
      <button (click)="this.query.set(queryInput.value); queryInput.value = ''">Generate</button>
      
    </div>
  `,
  styles: `
    input[type="text"] {
      width: 300px;
      padding: 8px;
      margin-right: 10px;
    }
    button {
      padding: 8px 16px;
    }
    pre.generated-response {
      padding: 12px;
      border: 1px solid #ccc;
      border-radius: 4px;
      background: #fafafa;
      white-space: pre-wrap;
      word-break: break-word;
    }
  `,
})
export class Home {
  private http = inject(HttpClient);

  mcpHost = 'https://localhost:7054';

  remoteHistory = httpResource<HistoryDto>(() => `${this.mcpHost}/history`);

  query = signal<string>('');

  stream = resource({
    params: this.query,
    stream: async (loaderParams) => {
      const query = loaderParams.params;
      if (!query) {
        return signal<ResourceStreamItem<string>>({ value: "" });
      }

      let resolve: (value: WritableSignal<ResourceStreamItem<string>> | PromiseLike<WritableSignal<ResourceStreamItem<string>>>) => void;
      const promise = new Promise<WritableSignal<ResourceStreamItem<string>>>((res) => {
        resolve = res;
      });
      const result = signal<ResourceStreamItem<string>>({ value: "" });

      this.http.post(`${this.mcpHost}/generate`, { query },{
        observe: 'events', 
        responseType: 'text', 
        reportProgress: true 
      }).subscribe({
        next: (event) => {
          if (event.type === HttpEventType.DownloadProgress) {
            const partial = (event as HttpDownloadProgressEvent).partialText ?? '';
            result.set({ value: partial });
            resolve(result);
          }
          else if (event.type === HttpEventType.Response) {
            this.streamDone.set({ query, response: event });
          }
          window.scrollTo(0,document.body.scrollHeight); //keep scrolled to bottom
        },
        error: (error) => {
          result.set({ error: error });
          resolve(result);
        },
        complete: () => {
          resolve(result);
        }
      });

      return promise;
    }
  })
  
  streamDone = signal<({query: string, response: HttpResponse<string>}) | undefined>(undefined); //because stream resource does not have a property to detect when its done.

  whenNewQuery = effect(() => {
    const query = this.query();
    if (query) {
      this.newHistory.update((history) => [...history, { role: 'user', text: query }])
    }
  });

  whenStreamDone = effect(() => {
    const streamDone = this.streamDone();
    if (streamDone) {
      const text = streamDone.response.body?.replace(/^assistant:\s*/, '') ?? '';
      this.newHistory.update((history) => [...history, { role: 'assistant', text: text }])
      this.stream.set(''); // Clear the stream after processing
    }
  });

  newHistory = signal<HistoryViewModel[]>([]);

  history = computed<HistoryViewModel[]>(() => {
    const remoteHistory = this.remoteHistory.value();
    const newHistory = this.newHistory();
    return [
      ...remoteHistory?.messages ?? [],
      ...newHistory
    ];
  });
}

interface HistoryViewModel {
  role: string;
  text: string;
}

interface HistoryDto {
  messages: MessageDto[];
}

interface MessageDto {
  role: string;
  text: string;
  generation: GenerationData;
}

interface GenerationData {
  generatedAt: Date;
}