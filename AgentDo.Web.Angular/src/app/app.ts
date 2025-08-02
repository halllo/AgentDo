import { Component, signal } from '@angular/core';
import { Home } from './home/home';

@Component({
  selector: 'app-root',
  imports: [Home],
  template: `
    <main>
      <header class="brand-name">
        <h1>{{ title() }}</h1>
      </header>
      <section class="content">
        <app-home></app-home>
      </section>
    </main>
  `,
  styleUrls: ['./app.scss']
})
export class App {
  protected readonly title = signal('AgentDo.Web.Angular');
}
