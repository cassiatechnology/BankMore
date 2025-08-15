CREATE TABLE IF NOT EXISTS transferencia (
	idtransferencia TEXT(37) PRIMARY KEY, -- identificacao unica da transferencia
	idcontacorrente_origem TEXT(37) NOT NULL, -- identificacao unica da conta corrente de origem
	idcontacorrente_destino TEXT(37) NOT NULL, -- identificacao unica da conta corrente de destino
	datamovimento TEXT(25) NOT NULL, -- data do transferencia no formato DD/MM/YYYY
	valor REAL NOT NULL -- valor da transferencia. Usar duas casas decimais.
	--FOREIGN KEY(idtransferencia) REFERENCES transferencia(idtransferencia)
);

CREATE TABLE IF NOT EXISTS idempotencia (
	chave_idempotencia TEXT(37) PRIMARY KEY, -- identificacao chave de idempotencia
	requisicao TEXT(1000), -- dados de requisicao
	resultado TEXT(1000) -- dados de retorno
);