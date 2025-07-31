import { HttpClient, HttpDownloadProgressEvent, HttpEventType } from '@angular/common/http';
import {Component, inject, resource, ResourceStreamItem, signal, WritableSignal} from '@angular/core';

@Component({
  selector: 'app-home',
  template: `
    <div>
      <input type="text" #queryInput placeholder="Enter your query..." (keydown.enter)="this.query.set(queryInput.value)"/>
      <button (click)="this.query.set(queryInput.value)">Generate</button>

      @if (this.stream.isLoading()) {
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

      this.http.get(`https://localhost:7054/generate?query=${query}`, {
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
}
