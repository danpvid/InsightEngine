import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime, timedelta

fake = Faker('pt_BR')

def generate_production_data(num_rows=5000):
    data = []

    # Distribuições realistas
    machines = ['Máquina A', 'Máquina B', 'Máquina C', 'Linha Automática 1', 'Linha Automática 2']
    machine_weights = [0.2, 0.2, 0.2, 0.2, 0.2]

    statuses = ['Concluída', 'Em Andamento', 'Parada', 'Cancelada']
    status_weights = [0.6, 0.3, 0.08, 0.02]

    shifts = ['Manhã', 'Tarde', 'Noite']
    shift_weights = [0.4, 0.35, 0.25]

    for i in range(num_rows):
        start_date = fake.date_between(start_date='-6m', end_date='today')
        planned_qty = random.randint(100, 10000)
        produced_qty = int(planned_qty * np.random.beta(8, 2))  # Geralmente produz mais que o planejado

        production_time = random.randint(1, 480)  # minutos
        efficiency = (produced_qty / planned_qty) * 100

        row = {
            'ID_Ordem': f'ORD{i+1:06d}',
            'Produto': fake.sentence(nb_words=3)[:-1],
            'Quantidade_Planejada': planned_qty,
            'Quantidade_Produzida': produced_qty,
            'Data_Inicio': start_date.strftime('%Y-%m-%d %H:%M'),
            'Data_Fim': (start_date + timedelta(minutes=production_time)).strftime('%Y-%m-%d %H:%M'),
            'Maquina': np.random.choice(machines, p=machine_weights),
            'Operador': fake.name(),
            'Tempo_Producao_Min': production_time,
            'Eficiencia': round(efficiency, 2),
            'Defeitos': random.randint(0, int(produced_qty * 0.05)),
            'Custo_Materia_Prima': round(np.random.lognormal(8, 1), 2),
            'Custo_Mao_Obra': round(np.random.lognormal(7, 0.8), 2),
            'Status': np.random.choice(statuses, p=status_weights),
            'Linha_Producao': f'Linha {random.randint(1, 10)}',
            'Turno': np.random.choice(shifts, p=shift_weights),
            'Qualidade': random.choice(['A', 'B', 'C', 'D']),
            'Observacoes': fake.sentence() if random.random() > 0.8 else None
        }
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/producao_manufatura.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'producao_manufatura.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_production_data()