import chalk from 'chalk';
import cliui from 'cliui';
import {Command} from 'commander';
import fs from 'node:fs';
import yargs from 'yargs';

const program = new Command();

program
	.name('okojo-tool')
	.description('Real CLI app sandbox for Okojo.Node')
	.showHelpAfterError();

program
	.command('greet')
	.description('Print a styled greeting')
	.option('--name <name>', 'name to greet', ')
	.option('--times <n>', 'number of greetings', value => Number.parseInt(value, 10), 1)
	.action(options => {
		const lines = [];
		for (let i = 0; i < options.times; i++) {
			lines.push(chalk.green(`hello:${options.name}:${i + 1}`));
		}

		process.stdout.write(lines.join('\n') + '\n');
	});

program
	.command('inspect [items...]')
	.description('Parse a small sub-command payload with yargs')
	.allowUnknownOption(true)
	.action(() => {
		const rawArgs = process.argv.slice(3);
		const parsed = yargs(rawArgs)
			.exitProcess(false)
			.option('upper', {type: 'boolean', default: false})
			.option('repeat', {type: 'number', default: 1})
			.parse();

		const positionalItems = parsed._.map(value => String(value));
		const base = positionalItems.length === 0 ? ['(none)'] : positionalItems;
		const normalized = parsed.upper
			? base.map(item => item.toUpperCase())
			: base;

		const rendered = [];
		for (let i = 0; i < parsed.repeat; i++) {
			rendered.push(chalk.cyan(`items:${normalized.join(',')}`));
		}

		process.stdout.write(rendered.join('\n') + '\n');
	});

program
	.command('report')
	.description('Load a small config file and print a report')
	.option('--config <path>', 'config path', 'app.config.json')
	.action(options => {
		const config = JSON.parse(fs.readFileSync(options.config, 'utf8'));
		const title = chalk.bold(config.title);
		const lines = config.tasks.map((task, index) => chalk.yellow(`${index + 1}. ${task}`));
		process.stdout.write(`${title}\n${lines.join('\n')}\n`);
	});

program
	.command('explain')
	.description('Render a project explanation document with CLI layout')
	.option('--doc <path>', 'document path', 'project.explain.json')
	.option('--width <n>', 'layout width', value => Number.parseInt(value, 10), 68)
	.action(options => {
		const doc = JSON.parse(fs.readFileSync(options.doc, 'utf8'));
		const ui = cliui({
			width: options.width,
			wrap: true
		});

		ui.div(
			{text: chalk.bold(doc.name), width: 16, padding: [0, 1, 0, 0]},
			{text: chalk.cyan(doc.tagline), width: options.width - 16}
		);
		ui.div();
		ui.div({text: doc.summary, padding: [0, 0, 1, 0]});

		for (const section of doc.sections) {
			ui.div(
				{text: chalk.yellow(section.title), width: 16, padding: [0, 1, 0, 0]},
				{text: section.items.map(item => `- ${item}`).join('\n'), width: options.width - 16}
			);
			ui.div();
		}

		ui.div({text: chalk.green('Next Steps')});
		ui.div({text: doc.nextSteps.map((item, index) => `${index + 1}. ${item}`).join('\n')});
		process.stdout.write(ui.toString() + '\n');
	});

await program.parseAsync(process.argv);

export default 'ok';
