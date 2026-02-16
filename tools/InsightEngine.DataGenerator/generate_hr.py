import pandas as pd
import numpy as np
from faker import Faker
import random
from datetime import datetime, timedelta

fake = Faker('pt_BR')

def generate_hr_data(num_rows=5000):
    data = []

    # Distribuições realistas
    statuses = ['Ativo', 'Demitido', 'Afastado', 'Férias']
    status_weights = [0.8, 0.15, 0.03, 0.02]

    departments = ['Vendas', 'TI', 'RH', 'Financeiro', 'Operações']
    dept_weights = [0.25, 0.2, 0.1, 0.15, 0.3]

    genders = ['Masculino', 'Feminino', 'Outro']
    gender_weights = [0.48, 0.5, 0.02]

    education_levels = ['Ensino Fundamental', 'Ensino Médio', 'Superior', 'Pós-graduação']
    education_weights = [0.1, 0.4, 0.4, 0.1]

    for i in range(num_rows):
        admission_date = fake.date_between(start_date='-10y', end_date='today')
        age = random.randint(18, 65)
        salary = round(np.random.lognormal(9, 0.8), 2)  # Salários em distribuição log-normal

        row = {
            'ID_Funcionario': f'FUN{i+1:05d}',
            'Nome': fake.name(),
            'CPF': fake.cpf(),
            'Data_Admissao': admission_date.strftime('%Y-%m-%d'),
            'Cargo': fake.job(),
            'Salario': salary,
            'Departamento': np.random.choice(departments, p=dept_weights),
            'Status': np.random.choice(statuses, p=status_weights),
            'Data_Demissao': (admission_date + timedelta(days=random.randint(365, 3650))).strftime('%Y-%m-%d') if random.random() > 0.8 else None,
            'Idade': age,
            'Genero': np.random.choice(genders, p=gender_weights),
            'Escolaridade': np.random.choice(education_levels, p=education_weights),
            'Estado_Civil': random.choice(['Solteiro', 'Casado', 'Divorciado', 'Viúvo']),
            'Dependentes': random.randint(0, 5),
            'Horas_Extras': random.randint(0, 40) if random.random() > 0.6 else 0,
            'Faltas': random.randint(0, 30),
            'Avaliacao': round(random.uniform(1, 5), 1),
            'Beneficios': ', '.join(random.sample(['Vale Alimentação', 'Plano de Saúde', 'Vale Transporte', 'Seguro de Vida'], random.randint(1, 4))),
            'Cidade': fake.city(),
            'Estado': fake.state_abbr()
        }
        data.append(row)

    df = pd.DataFrame(data)
    df.to_csv('../../samples/recursos_humanos.csv', index=False, encoding='utf-8-sig')
    print(f"Arquivo 'recursos_humanos.csv' gerado com {num_rows} linhas.")

if __name__ == "__main__":
    generate_hr_data()