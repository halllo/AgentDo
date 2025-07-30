import { httpResource } from '@angular/common/http';
import {Component, signal} from '@angular/core';

@Component({
  selector: 'app-home',
  template: `
    <div>
      <input type="text" #queryInput placeholder="Enter your query..." (keydown.enter)="this.query.set(queryInput.value)"/>
      <button (click)="this.query.set(queryInput.value)">Generate</button>
      @if (this.generated.isLoading()) {
        <div class="loading-indicator">Generating...</div>
      }
      @let gen = this.generated.value();
      @if (gen) {
        <div class="generated-response">
          {{ gen }}
        </div>
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
    div.generated-response {
      margin-top: 16px;
      padding: 12px;
      border: 1px solid #ccc;
      border-radius: 4px;
      background: #fafafa;
    }
  `,
})
export class Home {
  query = signal<string>('');
  generated = httpResource.text(() => {
    const query = this.query();
    return !query ? undefined : `https://localhost:7054/generate?query=${query}`;
  });
}
